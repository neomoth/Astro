using Astro.Abstractions;
using Astro.Bot.Configuration;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

public class HelpCommand : Command
{
	public override string Name => "help";
	public override string Description => "Get help about a command, or show a list of all commands!";
	public override async Task ExecuteCommand(SocketSlashCommand command)
	{
		// await command.RespondAsync("\u2699\ufe0f Fetching command list...",
		// 	embed: null, components: null, ephemeral: true);
		await command.DeferAsync(ephemeral: true);
		var msg = await command.GetOriginalResponseAsync();
		var paginator = new Paginator(new Callback<Embed>(BuildEmbed), msg, GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"), CommandManager.Commands.Count);
		await msg.ModifyAsync(m =>
		{
			m.Content = "";
			m.Components = paginator.BuildComponents(0, msg.Id);
			m.Embed = BuildEmbed(0, command);
		});
		AstroBot.Client.ButtonExecuted += paginator.OnButtonExecuted;
	}

	private Embed BuildEmbed(int page, SocketInteraction context)
	{
		var commands = GetPageInfo(page);
		var builder = new EmbedBuilder()
			.WithTitle("-- commands --")
			.WithDescription("tip: type `/help` followed by the name of a command to see more about it.\nIf a command or subcommand doesn't show up here, you likely don't have permission to execute it.")
			.WithColor(Color.Purple);
		foreach (var command in from command in commands
		         let canExecute = CommandManager.AllowedToExecute(command, context)
		         where !command.HideIfCantExecute || canExecute
		         select command)
		{
			var allowedSubCommands = command.SubCommands.Where(cmd =>
			{
				var hasPerms = CommandManager.AllowedToExecute(cmd, context);
				switch (hasPerms)
				{
					case true:
					case false when !cmd.HideIfCantExecute:
						return true;
					default:
						return false;
				}
			});
			
			builder.AddField($"`/{command.Name}`",
				$"**Description:** {command.Description}{
					(command.SubCommands.Count > 0 ? $"\n**SubCommands:** {
						string.Join(", ", allowedSubCommands.Select(cmd => $"`{cmd.Name}`"))
					}" : string.Empty)
				}");
		}

		if (commands.Count / GlobalConfig.Get<int>("Bot.Help.CommandsPerPage") > 1)
			builder.WithFooter(
				$"Page {page + 1} out of {Math.Ceiling(commands.Count / (double)GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"))}");

		return builder.Build();
	}

	private List<ICommand> GetPageInfo(int page)
	{
		var pageSize = GlobalConfig.Get<int>("Bot.Help.CommandsPerPage");
		return CommandManager.Commands.Skip(page * pageSize).Take(pageSize).ToList();
	}
}