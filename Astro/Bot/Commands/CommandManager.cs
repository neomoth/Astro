using System.Reflection;
using Astro.Abstractions;
using Astro.Bot.Configuration;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using Serilog;

namespace Astro.Bot.Commands;

public static class CommandManager
{
	private static readonly Dictionary<Type, ICommand> CommandDict = [];
	private static readonly List<ICommand> RootCommandsList = [];
	public static IReadOnlyCollection<ICommand> Commands => CommandDict.Values;
	public static IReadOnlyCollection<ICommand> RootCommands => RootCommandsList;
	public static SocketSlashCommand? CurrentContext { get; private set; }

	static CommandManager()
	{
		HotReloadHandler.CommandHotReload += async (_, ev) =>
		{
			if (ev is not CommandHotReloadEventArgs args) return;
			Log.Information("Commands hot reloaded. Updated commands: {commands}",
				string.Join(',', args.LoadedTypes?.Select(t => t.Name).ToArray() ?? Array.Empty<string>()));
			await InitSlashCommands();
		};
	}
	
	public static void Load()
	{
		CommandDict.Clear();
		RootCommandsList.Clear();

		var allTypes = Assembly.GetExecutingAssembly().GetTypes()
			.Where(t =>
				typeof(ICommand).IsAssignableFrom(t) &&
				t.IsClass &&
				t is { IsAbstract: false, ContainsGenericParameters: false });

		var allCommands = new List<ICommand>();

		foreach (var type in allTypes)
		{
			var isRoot = type.IsDefined(typeof(RootCommandAttribute));
			var isGroup = type.IsDefined(typeof(CommandGroupAttribute));
			var isSub = type.IsDefined(typeof(SubCommandAttribute));

			if (!isRoot && !isGroup && !isSub) continue;

			var instance = (ICommand)Activator.CreateInstance(type)!;
			CommandDict[type] = instance;
			allCommands.Add(instance);
		}

		BuildTree(allCommands);

		Log.Verbose("Loaded {count} commands.", RootCommands.Count);
	}

	private static void BuildTree(List<ICommand> all)
	{
		foreach (var cmd in all)
		{
			var type = cmd.GetType();

			if (type.IsDefined(typeof(RootCommandAttribute)))
			{
				RootCommandsList.Add(cmd);
				continue;
			}

			if (type.GetCustomAttribute<CommandGroupAttribute>() is { } group)
			{
				Attach(cmd, group.Parent);
				continue;
			}

			if (type.GetCustomAttribute<SubCommandAttribute>() is { } sub)
			{
				Attach(cmd, sub.Parent);
				continue;
			}

			Log.Warning("Command {cmd} has no valid attribute and will not be attached.", type.Name);
		}
	}

	private static void Attach(ICommand child, Type type)
	{
		if (!CommandDict.TryGetValue(type, out var cmd))
			throw new InvalidOperationException(
				$"{child.GetType().Name} attempted to attach to invalid parent {type.Name}");
		cmd.SubCommands.Add(child);
	}

	public static async Task InitSlashCommands()
	{
		try
		{
			await AstroBot.Client.BulkOverwriteGlobalApplicationCommandsAsync(RootCommands
				.Select(root => BuildSlash(root).Build()).Cast<ApplicationCommandProperties>().ToArray());
			Log.Verbose("Slash commands registered.");
		}
		catch (HttpException e)
		{
			Log.Error("Got error while attempting to bulk register slash commands.\n{err}",
				JsonConvert.SerializeObject(e.Message, Formatting.Indented));
		}
	}

	private static SlashCommandBuilder BuildSlash(ICommand root)
	{
		var builder = new SlashCommandBuilder()
			.WithName(root.Name)
			.WithDescription(root.Description)
			.WithNsfw(root.IsNsfw)
			.AddOptions(root.Options.ToArray());

		foreach (var sub in root.SubCommands)
			builder.AddOption(BuildOption(sub));

		return builder;
	}

	private static SlashCommandOptionBuilder BuildOption(ICommand cmd)
	{
		var type = cmd.SubCommands.Count > 0
			? ApplicationCommandOptionType.SubCommandGroup
			: ApplicationCommandOptionType.SubCommand;

		var builder = new SlashCommandOptionBuilder()
			.WithName(cmd.Name)
			.WithDescription(cmd.Description)
			.WithType(type);

		if (type is ApplicationCommandOptionType.SubCommand)
			builder.AddOptions(cmd.Options.ToArray());

		foreach (var sub in cmd.SubCommands)
			builder.AddOption(BuildOption(sub));

		return builder;
	}

	public static async Task HandleCommand(SocketSlashCommand command)
	{
		CurrentContext = command;
		var root = RootCommands.FirstOrDefault(r=> r.Name.Equals(command.CommandName, StringComparison.OrdinalIgnoreCase));

		if (root is null)
		{
			Log.Warning(
				"Command {name} has no associated command handler. How you managed this is beyond me but please create it.",
				command.CommandName);
			CurrentContext = null;
			return;
		}

		var current = root;
		var opts = command.Data.Options;

		while (opts.Count > 0)
		{
			var nextOpt = opts.First();
			var next = current.SubCommands.FirstOrDefault(s =>
				s.Name.Equals(nextOpt.Name, StringComparison.OrdinalIgnoreCase));

			if (next is null) break;
			if (!AllowedToExecute(next, command))
			{
				await command.RespondAsync(next.RejectMessage, ephemeral: true);
				CurrentContext = null;
				return;
			}

			current = next;
			opts = nextOpt.Options;
		}

		if (current.SubCommands.Count > 0)
			throw new InvalidOperationException(
				$"Command groups {current.Name} has no subcommands and attempted to execute.");

		if (!AllowedToExecute(current, command))
		{
			await command.RespondAsync(current.RejectMessage, ephemeral: true);
			CurrentContext = null;
			return;
		}

		var args = CommandArgs.Create(command);
		await current.ExecuteCommand(command, args);
		CurrentContext = null;
	}

	public static bool AllowedToExecute(ICommand checkAgainst, SocketInteraction context)
	{
		if (context.GuildId is null) return false;
		var user = (SocketGuildUser)context.User;
		var channel = (SocketGuildChannel)context.Channel;
		
		if (checkAgainst.RequiresWhitelist)
		{
			var whitelist = GlobalConfig.Get<ulong[]>("Bot.GlobalWhitelist.Whitelist") ?? [];
			if (!whitelist.Contains(user.Id)) return false;
			//TODO: add guild whitelist check here later once the database is implemented.
		}
		
		if (user.GuildPermissions.Administrator) return true;
		
		var guildPerms = user.GuildPermissions;
		var channelPerms = user.GetPermissions(channel);

		return checkAgainst.RequiredPermissions.Where(perm => !guildPerms.Has(perm))
			.All(perm => channelPerms.Has((ChannelPermission)perm));
	}
}

public abstract class Command : ICommand
{
	public abstract string Name { get; }
	public abstract string Description { get; }
	public virtual bool IsNsfw => false;
	public virtual bool HideIfCantExecute => false;
	public virtual List<SlashCommandOptionBuilder> Options { get; } = [];

	public virtual List<ICommand> SubCommands { get; } = [];

	public virtual List<GuildPermission> RequiredPermissions => [];
	public virtual bool RequiresWhitelist => false;
	public virtual string RejectMessage => "You do not have permission to execute this command.";

	protected virtual Task ExecuteCommand(SocketSlashCommand command, CommandArgs args) =>
		throw new InvalidOperationException("No behavior is defined for this command.");
	Task ICommand.ExecuteCommand(SocketSlashCommand command, ICommandArgs commandArgs)
	{
		return ExecuteCommand(command, commandArgs as CommandArgs ?? CommandArgs.Create(command));
	}

	public void Dispose()
	{
		foreach (var subCommand in SubCommands)
		{
			subCommand.Dispose();
		}

		SubCommands.Clear();
		Options.Clear();
		RequiredPermissions.Clear();
		GC.SuppressFinalize(this);
	}
}

public abstract class CommandGroup : Command, ICommandGroup
{
	protected sealed override Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
		=> throw new InvalidOperationException(
			"Command groups are not allowed to execute directly. Add subcommands to this group to execute instead.");
}

public sealed class CommandHotReloadEventArgs : EventArgs
{
	public Type[]? LoadedTypes { get; set; }
}

public static class HotReloadHandler
{
	public static void UpdateApplication(Type[]? updatedTypes)
	{
		if(updatedTypes is null) return;
		var updatedCommands = updatedTypes.Where(t => typeof(ICommand).IsAssignableFrom(t));
		var enumerable = updatedCommands.ToList();
		if (enumerable.Count == 0) return;
		CommandHotReload?.Invoke(null, new CommandHotReloadEventArgs { LoadedTypes = enumerable.ToArray() });
	}

	public static event EventHandler? CommandHotReload;
}