using Discord.WebSocket;

namespace Astro.Bot.Commands;

public class CycleRandomStatusCommand : Command
{
	public override string Name => "cyclerandomstatus";
	public override string Description => "Forces the random status to update.";
	public override bool RequiresWhitelist => true;
	public override bool HideIfCantExecute => true;

	public override async Task ExecuteCommand(SocketSlashCommand command)
	{
		await command.DeferAsync(ephemeral: true);
		await RandomStatus.CycleStatus();
		await command.FollowupAsync("Cycled the custom status.");
	}
}