using Astro.Abstractions;
using Astro.Bot.Configuration;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

[RootCommand]
public class HelpCommand : Command
{
	public override string Name => "help";
	public override string Description => "Get help about a command, or show a list of all commands!";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("path").WithDescription("Get help with a subcommand, command group, or subcommand from a group.")
			.WithType(ApplicationCommandOptionType.String)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var msg = await command.GetOriginalResponseAsync();
		if (command.Data.Options.Count == 0)
		{
			var paginator = new Paginator(new Callback<Embed>(BuildRootCommandList), msg,
				GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"), CommandManager.RootCommands.Count, command);
			await msg.ModifyAsync(m =>
			{
				m.Content = string.Empty;
				m.Components = paginator.BuildComponents(0, msg.Id);
				m.Embed = BuildRootCommandList(0, command, command);
			});
			AstroBot.Client.ButtonExecuted += paginator.OnButtonExecuted;
			return;
		}

		var argSplit = args.Get<string>("path")?.Split(' ');
		if (argSplit is null) throw new ArgumentNullException($"{argSplit} is null");
		if (argSplit.Length == 1)
		{
			var cmd = CommandManager.Commands.FirstOrDefault(c =>
				c.Name.Equals(argSplit[0], StringComparison.OrdinalIgnoreCase));
			if (cmd is null)
			{
				await msg.ModifyAsync(m => m.Content = "Command not found.");
				return;
			}

			await msg.ModifyAsync(m =>
			{
				m.Content = string.Empty;
				m.Embed = BuildCommandInfo(cmd, command, string.Join(' ', argSplit).ToLower());
				m.Components = null;
			});
			return;
		}

		if (argSplit.Length == 2)
		{
			var cmd = CommandManager.Commands.FirstOrDefault(c =>
				c.Name.Equals(argSplit[1], StringComparison.OrdinalIgnoreCase));
			if (cmd is null)
			{
				await msg.ModifyAsync(m => m.Content = "Command or group not found.");
				return;
			}

			if (cmd.SubCommands.Count > 0)
			{
				var subCommands = cmd.SubCommands
					.Where(sc => !sc.HideIfCantExecute || CommandManager.AllowedToExecute(sc, command))
					.ToList();

				if (subCommands.Count == 0)
				{
					await msg.ModifyAsync(m => m.Embed = new EmbedBuilder()
						.WithTitle("No subcommands")
						.WithDescription($"Subcommand group `{argSplit[1]}` has no accessible subcommands.")
						.WithColor(Color.Red).Build());
					return;
				}

				var paginator = new Paginator(new Callback<Embed>(BuildSubcommandList), msg,
					GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"), subCommands.Count, command);

				await msg.ModifyAsync(m =>
				{
					m.Content = "";
					m.Components = paginator.BuildComponents(0, msg.Id);
					m.Embed = BuildSubcommandList(0, command, command);
				});

				AstroBot.Client.ButtonExecuted += paginator.OnButtonExecuted;
				return;
			}
			await msg.ModifyAsync(m =>
			{
				m.Content = string.Empty;
				m.Embed = BuildCommandInfo(cmd, command, string.Join(' ', argSplit).ToLower());
				m.Components = null;
			});
			return;
		}
		
		var sub = CommandManager.Commands.FirstOrDefault(c =>
			c.Name.Equals(argSplit[2], StringComparison.OrdinalIgnoreCase));

		if (sub is null)
		{
			await msg.ModifyAsync(m => m.Content = "Command not found.");
			return;
		}
		
		await msg.ModifyAsync(m =>
		{
			m.Content = "";
			m.Embed = BuildCommandInfo(sub, command, string.Join(' ', argSplit).ToLower());
			m.Components = null;
		});
	}

	private static Embed BuildRootCommandList(int page, SocketInteraction context, SocketInteraction originalContext)
	{
		var pageSize = GlobalConfig.Get<int>("Bot.Help.CommandsPerPage");
		var commands = CommandManager.RootCommands.Skip(page * pageSize).Take(pageSize).ToList();
		var builder = new EmbedBuilder()
			.WithTitle("-- commands --")
			.WithDescription(
				"tip: type `/help` followed by the name of a command to see more about it.\nIf a command or subcommand doesn't show up here, you likely don't have permission to execute it.")
			.WithColor(Color.Purple);
		foreach (var command in from command in commands
		         let canExecute = CommandManager.AllowedToExecute(command, context)
		         where !command.HideIfCantExecute || canExecute
		         select command)
		{
			var parameters = command.Options.Where(opt =>
					opt.Type != ApplicationCommandOptionType.SubCommand &&
					opt.Type != ApplicationCommandOptionType.SubCommandGroup)
				.Select(opt => $"`{opt.Name}`").ToArray();

			var subs = command.SubCommands.Where(sc => sc.SubCommands.Count == 0 && CanShow(sc, context))
				.Select(sc => $"`{sc.Name}`").ToArray();

			var groups = command.SubCommands.Where(sc => sc.SubCommands.Count > 0 && CanShow(sc, context))
				.Select(group => $"`{group.Name}`").ToArray();

			builder.AddField($"`/{command.Name}`",
				$"**Description**: {command.Description}" +
				$"{(parameters.Length != 0 ? $"\n**Parameters**: {string.Join(", ", parameters)}" : string.Empty)}" +
				$"{(subs.Length != 0 ? $"\n**Sub Commands**: {string.Join(", ", subs)}" : string.Empty)}" +
				$"{(groups.Length != 0 ? $"\n**Command Groups**: {string.Join(", ", groups)}" : string.Empty)}"
			);
		}

		if (commands.Count / GlobalConfig.Get<int>("Bot.Help.CommandsPerPage") > 1)
			builder.WithFooter(
				$"Page {page + 1} out of {Math.Ceiling(commands.Count / (double)GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"))}");

		return builder.Build();
	}

	private static Embed BuildSubcommandList(int page, SocketInteraction context, SocketInteraction originalContext)
	{
		var pageSize = GlobalConfig.Get<int>("Bot.Help.CommandsPerPage");
		var path = (string)((SocketSlashCommand)originalContext).Data.Options.First();
		var pathSplit = path.Split(' ');
		var cmd = CommandManager.Commands.FirstOrDefault(c =>
			c.Name.Equals(pathSplit[1], StringComparison.OrdinalIgnoreCase));
		if (cmd is null) throw new ArgumentException($"Command {pathSplit[1]} not found.");
		var commands = cmd.SubCommands.Skip(page * pageSize).Take(pageSize).ToList();
		var builder = new EmbedBuilder()
			.WithTitle("-- subcommands --")
			.WithDescription(
				$"Showing subcommands for `/{path}`")
			.WithColor(Color.Purple);
		foreach (var command in from command in commands
		         let canExecute = CommandManager.AllowedToExecute(command, context)
		         where !command.HideIfCantExecute || canExecute
		         select command)
		{
			var parameters = command.Options.Where(opt =>
					opt.Type != ApplicationCommandOptionType.SubCommand &&
					opt.Type != ApplicationCommandOptionType.SubCommandGroup)
				.Select(opt => $"`{opt.Name}`").ToArray();

			builder.AddField($"`{command.Name}`",
				$"**Description**: {command.Description}" +
				$"{(parameters.Length != 0 ? $"\n**Parameters**: {string.Join(", ", parameters)}" : string.Empty)}"
			);
		}

		if (commands.Count / GlobalConfig.Get<int>("Bot.Help.CommandsPerPage") > 1)
			builder.WithFooter(
				$"Page {page + 1} out of {Math.Ceiling(commands.Count / (double)GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"))}");

		return builder.Build();
	}

	private Embed BuildCommandInfo(ICommand cmd, SocketInteraction context, string path)
	{
		var builder = new EmbedBuilder()
			.WithTitle($"/{path}")
			.WithDescription(cmd.Description)
			.WithColor(Color.Purple);

		var parameters = cmd.Options.Where(opt =>
				opt.Type != ApplicationCommandOptionType.SubCommand &&
				opt.Type != ApplicationCommandOptionType.SubCommandGroup)
			.Select(opt => $"`{opt.Name}`").ToArray();

		var subs = cmd.SubCommands.Where(sc => sc.SubCommands.Count == 0 && CanShow(sc, context))
			.Select(sc => $"`{sc.Name}`").ToArray();

		var groups = cmd.SubCommands.Where(sc => sc.SubCommands.Count > 0 && CanShow(sc, context))
			.Select(group => $"`{group.Name}`").ToArray();

		if(parameters.Length>0) builder.AddField("Parameters", string.Join(", ", parameters));
		if(subs.Length>0) builder.AddField("Parameters", string.Join(", ", subs));
		if(groups.Length>0) builder.AddField("Parameters", string.Join(", ", groups));

		return builder.Build();
	}

	private static bool CanShow(ICommand cmd, SocketInteraction context)
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
	}
}