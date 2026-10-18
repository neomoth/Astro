namespace Astro.Abstractions;

/// <summary>
/// Places a command can be run from.
/// </summary>
[Flags]
public enum CommandLocation
{
	None = 0,

	/// <summary>Servers the bot has been added to.</summary>
	Server = 1 << 0,

	/// <summary>Servers the bot hasn't been added to, through a user install. Guild permissions can't be checked here.</summary>
	OtherServers = 1 << 1,

	/// <summary>DMs with the bot.</summary>
	BotDm = 1 << 2,

	/// <summary>Other DMs and group DMs, through a user install.</summary>
	PrivateChannels = 1 << 3,

	Everywhere = Server | OtherServers | BotDm | PrivateChannels,
}

/// <summary>
/// Limits where a command can be run from (by default, <see cref="CommandLocation.Everywhere"/>), or where a behavior
/// acts (by default, <see cref="CommandLocation.Server"/> only). Inherited by
/// everything under it unless a group or subcommand declares its own, the same way
/// <see cref="RequirePermissionsAttribute"/> is.
/// </summary>
/// <remarks>
/// Commands are hidden from <c>/help</c> anywhere they aren't available. Discord registers locations per root command,
/// so a root is offered anywhere at least one command in its tree is available; the rest are rejected when run.
/// <para/>
/// Guild permissions from <see cref="RequirePermissionsAttribute"/> can only be checked in
/// <see cref="CommandLocation.Server"/>, so commands requiring them fail everywhere else (unless global flags let the
/// user bypass them). Put server-specific parts of a command in a group marked <see cref="CommandLocation.Server"/> to
/// keep the rest usable anywhere.
/// </remarks>
/// <example>
/// <code>
/// [CommandGroup(typeof(ConfigCommand))]
/// [AvailableIn(CommandLocation.Server)]
/// public sealed class ConfigServerSubCommandGroup : CommandGroup;
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AvailableInAttribute(CommandLocation locations) : Attribute
{
	public CommandLocation Locations => locations;
}
