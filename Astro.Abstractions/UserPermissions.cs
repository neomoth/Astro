namespace Astro.Abstractions;

/// <summary>
/// Bot-wide permissions a user can be granted, independent of their Discord permissions in any guild.
/// Combine with <c>|</c>, check with <c>HasFlag</c>. Each new member needs the next unused bit (1UL &lt;&lt; n).
/// </summary>
[Flags]
public enum UserPermissions : ulong
{
	/// <summary>
	/// No permissions.
	/// </summary>
	None = 0,

	/// <summary>
	/// Able to edit global configuration file.
	/// </summary>
	Config = 1UL << 0,

	/// <summary>
	/// Able to view bot secrets.
	/// </summary>
	Secrets = 1UL << 1,

	/// <summary>
	/// Able to perform moderator actions globally, irrespective of permission level on a given guild.
	/// </summary>
	GlobalModerator = 1UL << 2,

	/// <summary>
	/// Able to perform admin actions globally, irrespective of permission level on a given guild.
	/// </summary>
	GlobalAdmin = 1UL << 3,

	/// <summary>
	/// Able to view personally identifiable information about users.
	/// </summary>
	Pii = 1UL << 61,

	/// <summary>
	/// Dangerous permissions like global database management or particularly dangerous config values.
	/// </summary>
	Danger = 1UL << 62,

	/// <summary>
	/// Host permissions, only hoster of bot instance should be granted this flag. Typically, implicitly
	/// grants all permissions, however the host should probably just be given all flags.
	/// </summary>
	Host = 1UL << 63,

	/// <summary>
	/// Represents all permission flags. Should only be used for hoster of bot instance.
	/// </summary>
	All = ulong.MaxValue
}
