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
	private static readonly Dictionary<Type, ICommand> _commands = [];
	public static IReadOnlyCollection<ICommand> Commands => _commands.Values;

	static CommandManager()
	{
		HotReloadHandler.CommandHotReload += async (_, ev) =>
		{
			if (ev is not CommandHotReloadEventArgs args) return;
			Log.Information(
				$"Commands hot reloaded. Updated commands: {string.Join(',', args.LoadedTypes?.Select(t => t.Name).ToArray() ?? Array.Empty<string>())}");
			await InitSlashCommands();
		};
	}
	
	public static void Load()
	{
		Log.Verbose("Loading commands...");

		foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
		{
			if (!typeof(ICommand).IsAssignableFrom(type)) continue;
			if (typeof(ISubCommand).IsAssignableFrom(type)) continue;
			if (!type.IsClass || type.IsAbstract) continue;
			if (type.ContainsGenericParameters) continue;
			var instance = (ICommand)Activator.CreateInstance(type)!;
			Log.Information("Registered command {TypeName}", type.Name);
			_commands[type] = instance;
		}
		
		Log.Information("Loaded {Count} commands.", _commands.Count);
	}

	public static async Task InitSlashCommands()
	{
		var slashCommands = new List<ApplicationCommandProperties>();
		foreach (var cmd in _commands.Values)
		{
			var builder = new SlashCommandBuilder().WithName(cmd.Name)
				.WithDescription(cmd.Description)
				.WithNsfw(cmd.IsNsfw)
				.AddOptions(cmd.Options.ToArray());
			foreach (var sub in cmd.SubCommands)
			{
				builder.AddOption(sub.Build());
			}
			slashCommands.Add(builder.Build());
		}
		if (slashCommands.Count != 0)
		{
			try
			{
				await AstroBot.Client.BulkOverwriteGlobalApplicationCommandsAsync(slashCommands.ToArray());
				Log.Information("Bulk created slash commands!");
			}
			catch (HttpException e)
			{
				var json = JsonConvert.SerializeObject(e.Errors, Formatting.Indented);
				Log.Error("Error while creating slash commands: {error}", json);
			}
		}
	}
	
	public static async Task HandleCommand(SocketSlashCommand command)
	{
		var cmd = Commands.FirstOrDefault(c => c.Name.Equals(command.CommandName, StringComparison.OrdinalIgnoreCase));
		if (cmd == null) return;

		await Execute(cmd, command.Data.Options);
		return;

		async Task Execute(IExecutableCommand current, IReadOnlyCollection<SocketSlashCommandDataOption> opts)
		{
			while (true)
			{
				if (opts.Count != 0 && current is Command cmd1)
				{
					var firstOption = opts.First();
					var sub = cmd1.SubCommands.FirstOrDefault(s => s.Name.Equals(firstOption.Name, StringComparison.OrdinalIgnoreCase));
					if (sub != null)
					{
						if (!AllowedToExecute(sub, command))
						{
							await command.RespondAsync(sub.RejectMessage, ephemeral: true);
							return;
						}

						current = sub;
						opts = firstOption.Options;
						continue;
					}
				}
				else if (opts.Count != 0 && current is SubCommandGroup group)
				{
					var firstOption = opts.First();
					var sub = group.SubCommands.FirstOrDefault(s => s.Name.Equals(firstOption.Name, StringComparison.OrdinalIgnoreCase));
					if (sub != null)
					{
						if (!AllowedToExecute(sub, command))
						{
							await command.RespondAsync(sub.RejectMessage, ephemeral: true);
							return;
						}

						current = sub;
						opts = firstOption.Options;
						continue;
					}
				}

				// Base case: we reached an executable command
				if (current is IRequiresPermissions p && !AllowedToExecute(p, command))
				{
					await command.RespondAsync(p.RejectMessage, ephemeral: true);
					return;
				}

				await current.ExecuteCommand(command);
				break;
			}
		}
	}

	public static bool AllowedToExecute(IRequiresPermissions checkAgainst, SocketInteraction context)
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

	public abstract string Name { get; }
	public abstract string Description { get; }
	public virtual bool IsNsfw => false;
	public virtual bool HideIfCantExecute => false;
	public virtual string RejectMessage => "You do not have permission to execute this command.";
	public virtual List<SlashCommandOptionBuilder> Options => [];
	public virtual List<ISubCommand> SubCommands => [];
	public virtual List<GuildPermission> RequiredPermissions => [];
	public virtual bool RequiresWhitelist => false;
	public abstract Task ExecuteCommand(SocketSlashCommand command);
}

public abstract class SubCommand : ISubCommand
{
	public virtual SlashCommandOptionBuilder Build()
	{
		return new SlashCommandOptionBuilder()
			.WithName(Name)
			.WithDescription(Description)
			.WithType(ApplicationCommandOptionType.SubCommand)
			.AddOptions(Options.ToArray());
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

	public abstract string Name { get; }
	public abstract string Description { get; }
	public virtual List<GuildPermission> RequiredPermissions => [];
	public virtual bool RequiresWhitelist => false;
	public virtual bool HideIfCantExecute => false;
	public virtual string RejectMessage => "You do not have permission to execute this command.";
	public virtual List<SlashCommandOptionBuilder> Options => [];
	public virtual List<ISubCommand> SubCommands => [];
	public abstract Task ExecuteCommand(SocketSlashCommand command);
}

public abstract class SubCommandGroup : SubCommand
{
	public override SlashCommandOptionBuilder Build()
	{
		var builder = new SlashCommandOptionBuilder()
			.WithName(Name)
			.WithDescription(Description)
			.WithType(ApplicationCommandOptionType.SubCommandGroup);

		// Add all nested subcommands as options
		foreach (var sub in SubCommands)
			builder.AddOption(sub.Build());

		return builder;
	}
	/// <summary>
	/// DO NOT RUN THIS!! THIS IS A GROUP!! THERE IS NOTHING TO EXECUTE!! RAAHHH!!
	/// </summary>
	public override Task ExecuteCommand(SocketSlashCommand command) => Task.CompletedTask;
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