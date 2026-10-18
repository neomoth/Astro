using Discord;

namespace Astro.Abstractions;

/// <summary>
/// What holding a command's <see cref="RequirePermissionsAttribute.Global"/> flags means.
/// </summary>
public enum GlobalPermissionMode
{
	/// <summary>
	/// The global flags are needed to run the command at all, on top of its guild permissions.
	/// With no guild permissions specified, only the global flags matter.
	/// </summary>
	Required,

	/// <summary>Holding the global flags skips the guild permission check; everyone else needs the guild permissions.</summary>
	Bypass,
}

/// <summary>
/// Restricts who can run a command. Groups and subcommands without their own attribute inherit the nearest one above
/// them; declaring one on a group or subcommand replaces the inherited requirements for it and everything under it.
/// </summary>
/// <remarks>
/// <b>Guild permissions</b> (<see cref="Permissions"/>) are satisfied by either the user's guild permissions or their
/// permissions in the channel the command was run in. Guild administrators always pass them.
/// <para/>
/// <b>Global permissions</b> (<see cref="Global"/>) are the bot-wide <see cref="UserPermissions"/> stored per user.
/// All the given flags must be held; <see cref="UserPermissions.Host"/> counts as holding every flag.
/// <para/>
/// On root commands, the guild permissions are also sent to Discord as the command's default member permissions, so
/// users without them don't see it at all. Discord applies that to the whole command tree, so it's skipped when
/// anything under the root declares its own requirements, and in <see cref="GlobalPermissionMode.Bypass"/> mode, since
/// Discord would otherwise block users who should get through.
/// </remarks>
/// <example>
/// <code>
/// // Needs the Config flag, nothing else.
/// [RequirePermissions(Global = UserPermissions.Config)]
///
/// // Needs BanMembers in the guild, unless the user is a global moderator.
/// [RequirePermissions(GuildPermission.BanMembers, Global = UserPermissions.GlobalModerator, GlobalMode = GlobalPermissionMode.Bypass)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RequirePermissionsAttribute(GuildPermission permissions = 0) : Attribute
{
	public static readonly RequirePermissionsAttribute None = new();

	public GuildPermission Permissions => permissions;
	public UserPermissions Global { get; set; } = UserPermissions.None;
	public GlobalPermissionMode GlobalMode { get; set; } = GlobalPermissionMode.Required;

	/// <summary>
	/// Check the user's real global permissions even while they're spoofing having none. Only meant for the command
	/// that turns spoofing off, so it can't lock its user out.
	/// </summary>
	public bool IgnoreSpoof { get; set; }
}
