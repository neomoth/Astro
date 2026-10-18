namespace Astro.DB.Entities;

/// <summary>
/// Any class implementing this is automatically picked up as a database table, keyed by a Discord snowflake.
/// </summary>
public interface IConfigEntity
{
	/// <summary>
	/// The Discord ID this row belongs to (guild ID, user ID, etc.).
	/// </summary>
	public ulong Id { get; set; }
}
