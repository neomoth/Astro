using Astro.Abstractions;
using Astro.Bot.OAuth;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

[RootCommand]
public sealed class InviteCommand : Command
{
	[Dependency] private readonly OAuthManager _oauth = null!;

	public override string Name => "invite";
	public override string Description => "Add the bot to your account or to a server.";

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args) =>
		await command.RespondAsync(
			"**Add to My Apps** lets you use the bot's commands anywhere, including servers it isn't in and your DMs.\n" +
			"**Add to Server** adds the bot to a server you manage.",
			components: new ComponentBuilder()
				.WithButton("Add to My Apps", style: ButtonStyle.Link, url: _oauth.CreateInstallUrl(userInstall: true))
				.WithButton("Add to Server", style: ButtonStyle.Link, url: _oauth.CreateInstallUrl(userInstall: false))
				.Build(),
			ephemeral: true);
}
