using System.Reflection;
using Astro.Abstractions;
using Astro.Bot.Permissions;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Newtonsoft.Json;
using IoC;

namespace Astro.Bot.Commands;

public sealed class CommandManager : Manager
{
	[Dependency] private readonly AstroBot _bot = null!;
	[Dependency] private readonly UserPermissionManager _permissions = null!;
	[Dependency] private readonly IDependencyCollection _dependencies = null!;

	private readonly Dictionary<Type, ICommand> _commandDict = [];
	private readonly List<ICommand> _rootCommandsList = [];
	private readonly Dictionary<ICommand, RequirePermissionsAttribute> _effectiveRequirements = [];
	private readonly Dictionary<ICommand, CommandLocation> _effectiveLocations = [];
	public IReadOnlyCollection<ICommand> Commands => _commandDict.Values;
	public IReadOnlyCollection<ICommand> RootCommands => _rootCommandsList;
	public SocketSlashCommand? CurrentContext { get; private set; }

	protected override string SawmillName => "astro.commands";

	public override void PostInject() =>
		HotReloadHandler.CommandHotReload += async (_, ev) =>
		{
			if (ev is not CommandHotReloadEventArgs args) return;
			Log.Info("Commands hot reloaded. Updated commands: {commands}",
				string.Join(',', args.LoadedTypes?.Select(t => t.Name).ToArray() ?? Array.Empty<string>()));
			await InitSlashCommands();
		};

	public void Load()
	{
		_commandDict.Clear();
		_rootCommandsList.Clear();

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
			_dependencies.InjectDependencies(instance);
			_commandDict[type] = instance;
			allCommands.Add(instance);
		}

		BuildTree(allCommands);
		ResolveRequirements();

		Log.Verbose("Loaded {count} commands.", RootCommands.Count);
	}

	/// <summary>
	/// Works out what each command actually requires: its own [RequirePermissions], or else the nearest ancestor's.
	/// </summary>
	private void ResolveRequirements()
	{
		_effectiveRequirements.Clear();
		_effectiveLocations.Clear();
		foreach (var root in _rootCommandsList)
			Resolve(root, RequirePermissionsAttribute.None, CommandLocation.Everywhere);

		void Resolve(ICommand cmd, RequirePermissionsAttribute inherited, CommandLocation inheritedLocations)
		{
			var req = cmd.DeclaredRequirements ?? inherited;
			var locations = cmd.DeclaredLocations ?? inheritedLocations;
			_effectiveRequirements[cmd] = req;
			_effectiveLocations[cmd] = locations;
			if (cmd.DeclaredRequirements is { GlobalMode: GlobalPermissionMode.Bypass, Permissions: 0, Global: not UserPermissions.None })
				Log.Warn("{cmd} uses GlobalMode.Bypass without guild permissions, so anyone can run it.", cmd.GetType().Name);
			foreach (var sub in cmd.SubCommands)
				Resolve(sub, req, locations);
		}
	}

	/// <summary>The requirements that apply to <paramref name="cmd"/>, including ones inherited from its parents.</summary>
	public RequirePermissionsAttribute GetRequirements(ICommand cmd) =>
		_effectiveRequirements.GetValueOrDefault(cmd) ?? cmd.DeclaredRequirements ?? RequirePermissionsAttribute.None;

	/// <summary>Where <paramref name="cmd"/> can run, including any limit inherited from its parents.</summary>
	public CommandLocation GetLocations(ICommand cmd) =>
		_effectiveLocations.GetValueOrDefault(cmd, cmd.DeclaredLocations ?? CommandLocation.Everywhere);

	/// <summary>Where an interaction is happening.</summary>
	public CommandLocation GetLocation(SocketInteraction context) => context.ContextType switch
	{
		InteractionContextType.BotDm => CommandLocation.BotDm,
		InteractionContextType.PrivateChannel => CommandLocation.PrivateChannels,
		_ when context.GuildId is { } guildId =>
			_bot.Client.GetGuild(guildId) is not null ? CommandLocation.Server : CommandLocation.OtherServers,
		_ => CommandLocation.BotDm,
	};

	/// <summary>Whether <paramref name="cmd"/> can run where <paramref name="context"/> is happening.</summary>
	public bool IsAvailable(ICommand cmd, SocketInteraction context) => (GetLocations(cmd) & GetLocation(context)) != 0;

	private static IEnumerable<ICommand> Descendants(ICommand cmd) =>
		cmd.SubCommands.SelectMany(sub => Descendants(sub).Prepend(sub));

	private void BuildTree(List<ICommand> all)
	{
		foreach (var cmd in all)
		{
			var type = cmd.GetType();

			if (type.IsDefined(typeof(RootCommandAttribute)))
			{
				_rootCommandsList.Add(cmd);
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

			Log.Warn("Command {cmd} has no valid attribute and will not be attached.", type.Name);
		}
	}

	private void Attach(ICommand child, Type type)
	{
		if (!_commandDict.TryGetValue(type, out var cmd))
			throw new InvalidOperationException(
				$"{child.GetType().Name} attempted to attach to invalid parent {type.Name}");
		cmd.SubCommands.Add(child);
	}

	public async Task InitSlashCommands()
	{
		try
		{
			await _bot.Client.BulkOverwriteGlobalApplicationCommandsAsync(RootCommands
				.Select(root => BuildSlash(root).Build()).Cast<ApplicationCommandProperties>().ToArray());
			Log.Verbose("Slash commands registered.");
		}
		catch (HttpException e)
		{
			Log.Error("Got error while attempting to bulk register slash commands.\n{err}",
				JsonConvert.SerializeObject(e.Message, Formatting.Indented));
		}
	}

	private SlashCommandBuilder BuildSlash(ICommand root)
	{
		var builder = new SlashCommandBuilder()
			.WithName(root.Name)
			.WithDescription(root.Description)
			.WithNsfw(root.IsNsfw)
			.AddOptions(root.Options.ToArray());

		// Discord blocks users without these from the whole tree, so only send them when nothing under the root has its
		// own requirements and global flags can't grant access.
		var req = GetRequirements(root);
		if (req.Permissions != 0 &&
		    (req.Global == UserPermissions.None || req.GlobalMode is GlobalPermissionMode.Required) &&
		    Descendants(root).All(sub => sub.DeclaredRequirements is null))
			builder.WithDefaultMemberPermissions(req.Permissions);

		// Discord only knows locations per root, so offer it wherever anything in its tree can run.
		var locations = Descendants(root).Prepend(root).Aggregate(CommandLocation.None, (all, cmd) => all | GetLocations(cmd));
		builder.WithIntegrationTypes(ToIntegrationTypes(locations)).WithContextTypes(ToContextTypes(locations));

		foreach (var sub in root.SubCommands)
			builder.AddOption(BuildOption(sub));

		return builder;
	}

	private static ApplicationIntegrationType[] ToIntegrationTypes(CommandLocation locations)
	{
		var types = new List<ApplicationIntegrationType>();
		if ((locations & (CommandLocation.Server | CommandLocation.BotDm)) != 0)
			types.Add(ApplicationIntegrationType.GuildInstall);
		if ((locations & (CommandLocation.OtherServers | CommandLocation.PrivateChannels | CommandLocation.BotDm)) != 0)
			types.Add(ApplicationIntegrationType.UserInstall);
		return types.ToArray();
	}

	private static InteractionContextType[] ToContextTypes(CommandLocation locations)
	{
		var types = new List<InteractionContextType>();
		if ((locations & (CommandLocation.Server | CommandLocation.OtherServers)) != 0)
			types.Add(InteractionContextType.Guild);
		if (locations.HasFlag(CommandLocation.BotDm))
			types.Add(InteractionContextType.BotDm);
		if (locations.HasFlag(CommandLocation.PrivateChannels))
			types.Add(InteractionContextType.PrivateChannel);
		return types.ToArray();
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

	public async Task HandleCommand(SocketSlashCommand command)
	{
		CurrentContext = command;
		var root = RootCommands.FirstOrDefault(r=> r.Name.Equals(command.CommandName, StringComparison.OrdinalIgnoreCase));

		if (root is null)
		{
			Log.Warn(
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
			current = next;
			opts = nextOpt.Options;
		}

		if (current.SubCommands.Count > 0)
			throw new InvalidOperationException(
				$"Command groups {current.Name} has no subcommands and attempted to execute.");

		if (!IsAvailable(current, command))
		{
			await command.RespondAsync(GetLocations(current) == CommandLocation.Server
					? "This command only works in servers I've been added to."
					: "This command isn't available here.",
				ephemeral: true);
			CurrentContext = null;
			return;
		}

		// Only the command being run is checked: its requirements already include anything inherited from its parents.
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

	public bool AllowedToExecute(ICommand checkAgainst, SocketInteraction context)
	{
		if (!IsAvailable(checkAgainst, context)) return false;
		var user = context.User;

		var req = GetRequirements(checkAgainst);
		//TODO: add guild whitelist check here later once the database is implemented.
		if (req.Global != UserPermissions.None)
		{
			var hasGlobal = _permissions.Has(user.Id, req.Global, req.IgnoreSpoof);
			switch (req.GlobalMode)
			{
				case GlobalPermissionMode.Required when !hasGlobal:
					return false;
				case GlobalPermissionMode.Bypass when hasGlobal:
					return true;
			}
		}

		if (req.Permissions == 0) return true;
		// Only servers the bot is in give us a member with permissions to check.
		return user is SocketGuildUser member && context.Channel is SocketGuildChannel channel &&
		       HasPermissions(member, channel, req.Permissions);
	}

	private static bool HasPermissions(SocketGuildUser user, SocketGuildChannel channel, GuildPermission required)
	{
		if (user.GuildPermissions.Administrator) return true;

		var guildPerms = user.GuildPermissions;
		var channelPerms = user.GetPermissions(channel);

		return Enum.GetValues<GuildPermission>()
			.Where(perm => perm != 0 && required.HasFlag(perm) && !guildPerms.Has(perm))
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

	public CommandLocation? DeclaredLocations => GetType().GetCustomAttribute<AvailableInAttribute>()?.Locations;

	public RequirePermissionsAttribute? DeclaredRequirements =>
		GetType().GetCustomAttribute<RequirePermissionsAttribute>();
	public virtual string RejectMessage => "You do not have permission to execute this command.";

	protected virtual Task ExecuteCommand(SocketSlashCommand command, CommandArgs args) =>
		throw new InvalidOperationException("No behavior is defined for this command.");

	Task ICommand.ExecuteCommand(SocketSlashCommand command, ICommandArgs commandArgs) =>
		ExecuteCommand(command, commandArgs as CommandArgs ?? CommandArgs.Create(command));

	public void Dispose()
	{
		foreach (var subCommand in SubCommands)
		{
			subCommand.Dispose();
		}

		SubCommands.Clear();
		Options.Clear();
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
	public Type[]? LoadedTypes { get; init; }
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
