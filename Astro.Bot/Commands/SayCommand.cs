using Astro.Abstractions;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

[RootCommand]
[RequirePermissions(Global = UserPermissions.GlobalModerator)]
public sealed class SayCommand : Command
{
	public override string Name => "say";
	public override string Description => "Make the bot say something.";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("message").WithDescription("The message you want the bot to say.")
			.WithType(ApplicationCommandOptionType.String).WithRequired(true)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync();
		var msg = args.GetRequired<string>("message");
		await command.ModifyOriginalResponseAsync((prop) => prop.Content = msg);
	}
}
