using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Astro.DB.Entities;

/// <summary>
/// Per-guild configuration. Add a property here, run Tools/migrate.sh, done.
/// </summary>
public sealed class GuildConfig : IConfigEntity
{
	public ulong Id { get; set; }

	[Description("Channel the bot logs to in this server.")]
	public ulong? LoggingChannelId { get; set; }
	public List<ulong> Whitelist { get; set; } = [];
	[Description("Nickname the bot uses in the guild."), StringLength(32)]
	public string? GuildNickname { get; set; }
}
