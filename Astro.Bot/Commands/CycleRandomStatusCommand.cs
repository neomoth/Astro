using Astro.Abstractions;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

[RootCommand]
[RequirePermissions(Global = UserPermissions.Host)]
public sealed class CycleRandomStatusCommand : Command
{
	public override string Name => "cyclerandomstatus";
	public override string Description => "Forces the random status to update.";
	public override bool HideIfCantExecute => true;

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		await RandomStatus.CycleStatus();
		await command.FollowupAsync("Cycled the custom status.");
	}
}
