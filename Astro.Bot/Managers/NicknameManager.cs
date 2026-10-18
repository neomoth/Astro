using Astro.Abstractions;
using Astro.DB;
using Astro.DB.Entities;
using Discord;
using Discord.Net;
using IoC;

namespace Astro.Bot.Managers;

public sealed class NicknameManager : Manager
{
	[Dependency] private readonly AstroBot _bot = null!;

	protected override string SawmillName => "astro.nickname";

	public override void Initialize() => ConfigEvents.Subscribe<GuildConfig>(OnConfigLoaded);

	private async Task OnConfigLoaded(GuildConfig? guild, ConfigChange change)
	{
		if (guild is null) return;
		try
		{
			await _bot.Client.GetGuild(guild.Id).CurrentUser.ModifyAsync(u => u.Nickname = guild.GuildNickname);
			Log.Info($"Changed nickname in {guild.Id} to {guild.GuildNickname}.");
		}
		catch (HttpException e) when (e.DiscordCode == DiscordErrorCode.MissingPermissions)
		{
			Log.Warn($"Unable to change own nickname in guild {guild.Id}.");
		}
	}
}
