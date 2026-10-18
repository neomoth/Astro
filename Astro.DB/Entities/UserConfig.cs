using Astro.Abstractions;

namespace Astro.DB.Entities;

/// <summary>
/// Global (cross-guild) per-user configuration. Add a property here, run Tools/migrate.sh, done.
/// </summary>
public sealed class UserConfig : IConfigEntity
{
	public ulong Id { get; set; }

	/// <summary>
	/// Bot-wide permissions granted to this user, across every guild. Managed through /globalpermissions.
	/// </summary>
	[ConfigRestricted]
	public UserPermissions Permissions { get; set; }
}
