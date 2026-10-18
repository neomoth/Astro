using Astro.Abstractions;
using Astro.Bot.OAuth;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

[RootCommand]
public sealed class OAuthCommand : CommandGroup
{
	public override string Name => "oauth";
	public override string Description => "Link your Discord account to the bot.";
}

[SubCommand(typeof(OAuthCommand))]
public sealed class OAuthLinkSubCommand : Command
{
	[Dependency] private readonly OAuthManager _oauth = null!;

	public override string Name => "link";
	public override string Description => "Get a link to authorize the bot with your Discord account.";

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		if (!_oauth.IsRunning)
		{
			await command.RespondAsync("Account linking isn't set up on this bot.", ephemeral: true);
			return;
		}

		var url = _oauth.CreateAuthorizationUrl(command.User.Id);
		await command.RespondAsync(
			$"Authorize with the button below. The link works once, only for you, and expires in 10 minutes.\n" +
			$"Use **Add to My Apps** too if you want the bot's commands available anywhere, not just in its servers.\n" +
			$"-# Requested access: {string.Join(", ", _oauth.Scopes.Select(s => $"`{s}`"))}",
			components: new ComponentBuilder()
				.WithButton("Authorize", style: ButtonStyle.Link, url: url)
				.WithButton("Add to My Apps", style: ButtonStyle.Link, url: _oauth.CreateInstallUrl(userInstall: true))
				.Build(),
			ephemeral: true);
	}
}

[SubCommand(typeof(OAuthCommand))]
public sealed class OAuthUnlinkSubCommand : Command
{
	[Dependency] private readonly OAuthManager _oauth = null!;

	public override string Name => "unlink";
	public override string Description => "Revoke the bot's access to your Discord account.";

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var unlinked = await _oauth.Unlink(command.User.Id);
		await command.FollowupAsync(unlinked
				? "Unlinked. The bot no longer has access to your account."
				: "Your account isn't linked.",
			ephemeral: true);
	}
}

[SubCommand(typeof(OAuthCommand))]
public sealed class OAuthStatusSubCommand : Command
{
	[Dependency] private readonly OAuthManager _oauth = null!;

	public override string Name => "status";
	public override string Description => "Check whether your account is linked, and what the bot can access.";

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var scopes = await _oauth.GetGrantedScopes(command.User.Id);
		await command.FollowupAsync(scopes is null
				? "Your account isn't linked. Use `/oauth link` to link it."
				: $"Your account is linked. Granted access: {string.Join(", ", scopes.Select(s => $"`{s}`"))}",
			ephemeral: true);
	}
}
