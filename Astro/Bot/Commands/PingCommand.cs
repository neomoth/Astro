using Astro.Abstractions;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

[RootCommand]
public sealed class PingCommand : Command
{
	public override string Name => "ping";
	public override string Description => "ping the bot, check response time!";

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var response = await command.GetOriginalResponseAsync();
		if (response is null) return;
		var sentAt = command.CreatedAt;
		var respondedAt = response.CreatedAt;
		var diff = respondedAt - sentAt;
		await command.ModifyOriginalResponseAsync(msg =>
		{
			msg.Content = $"Pong! `Took {diff.TotalSeconds}s`";
		});
	}
}