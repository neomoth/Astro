using Astro.Abstractions;
using Astro.Bot.Configuration;
using Discord;
using Discord.Rest;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

[RootCommand]
public sealed class HelpCommand : Command
{
	[Dependency] private readonly AstroBot _bot = null!;
	[Dependency] private readonly CommandManager _cmd = null!;

	public override string Name => "help";
	public override string Description => "Get help about a command, or show a list of all commands!";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("path")
			.WithDescription("The command to get help with, e.g. \"config global set\". Spaces or periods both work.")
			.WithType(ApplicationCommandOptionType.String),
		new SlashCommandOptionBuilder().WithName("public")
			.WithDescription("Show the help to everyone in the channel instead of only you. Off by default.")
			.WithType(ApplicationCommandOptionType.Boolean)
	];

	private static int _pageSize => Math.Max(1, GlobalConfig.Get<int>("Bot.Help.CommandsPerPage"));

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: !(args.Get<bool?>("public") ?? false));
		var msg = await command.GetOriginalResponseAsync();

		var path = args.Get<string>("path");
		if (string.IsNullOrWhiteSpace(path))
		{
			var roots = _cmd.RootCommands.Where(c => CanShow([c], command)).ToList();
			await ShowPaged(msg, command, roots.Count, (page, context) => BuildCommandList([], roots, page, context));
			return;
		}

		// Hidden commands get the same answer as missing ones, so their existence isn't leaked.
		var chain = Resolve(path, out var searched);
		if (chain is null || !CanShow(chain, command))
		{
			await command.ModifyOriginalResponseAsync(m => m.Content = $"No command found at `/{searched}`.");
			return;
		}

		var cmd = chain[^1];
		if (cmd.SubCommands.Count == 0)
		{
			await command.ModifyOriginalResponseAsync(m =>
			{
				m.Content = string.Empty;
				m.Embed = BuildCommandInfo(chain);
				m.Components = null;
			});
			return;
		}

		var children = cmd.SubCommands.Where(sc => CanShow([..chain, sc], command)).ToList();
		if (children.Count == 0)
		{
			await command.ModifyOriginalResponseAsync(m => m.Embed = new EmbedBuilder()
				.WithTitle("No subcommands")
				.WithDescription($"`/{FullPath(chain)}` has no subcommands you can access.")
				.WithColor(Color.Red).Build());
			return;
		}

		await ShowPaged(msg, command, children.Count, (page, context) => BuildCommandList(chain, children, page, context));
	}

	/// <summary>
	/// Walks the command tree along <paramref name="path"/> (separated by spaces, periods or slashes).
	/// Returns every command from the root down, or null if any segment doesn't exist.
	/// </summary>
	private List<ICommand>? Resolve(string path, out string searched)
	{
		var segments = path.Split([' ', '.', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var chain = new List<ICommand>();
		IEnumerable<ICommand> level = _cmd.RootCommands;

		foreach (var segment in segments)
		{
			var next = level.FirstOrDefault(c => c.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
			if (next is null)
			{
				searched = string.Join(' ', chain.Select(c => c.Name).Append(segment.ToLowerInvariant()));
				return null;
			}

			chain.Add(next);
			level = next.SubCommands;
		}

		searched = FullPath(chain);
		return chain.Count == 0 ? null : chain;
	}

	private async Task ShowPaged(RestInteractionMessage msg, SocketSlashCommand command, int total,
		Func<int, SocketInteraction, Embed> build)
	{
		var paginator = new Paginator(
			new Callback<Embed>(new Func<int, SocketInteraction, SocketInteraction, Embed>((page, context, _) => build(page, context))),
			msg, _pageSize, total, command);

		// Not msg.ModifyAsync: for ephemeral replies that edits through the channel and fails with "Unknown Message"
		// unless msg is typed as RestInteractionMessage, whose ModifyAsync hides (rather than overrides) the base one.
		await command.ModifyOriginalResponseAsync(m =>
		{
			m.Content = string.Empty;
			m.Components = paginator.BuildComponents(0, msg.Id);
			m.Embed = build(0, command);
		});
		_bot.Client.ButtonExecuted += paginator.OnButtonExecuted;
	}

	/// <summary>
	/// One page of <paramref name="items"/>, the commands directly under <paramref name="parent"/> (empty for root).
	/// </summary>
	private Embed BuildCommandList(List<ICommand> parent, List<ICommand> items, int page, SocketInteraction context)
	{
		var builder = new EmbedBuilder().WithColor(Color.Purple);

		if (parent.Count == 0)
			builder.WithTitle("-- commands --")
				.WithDescription("tip: use `/help path:<command>` to see more about a command, e.g. `/help path:config global`.\n" +
				                 "If a command doesn't show up here, you likely don't have permission to execute it.");
		else
			builder.WithTitle($"/{FullPath(parent)}")
				.WithDescription(parent[^1].Description);

		foreach (var item in items.Skip(page * _pageSize).Take(_pageSize))
		{
			List<ICommand> chain = [..parent, item];
			var lines = new List<string> { item.Description };

			if (item.SubCommands.Count == 0)
				lines.Add($"**Usage**: `{Usage(chain)}`");
			else
			{
				var subs = item.SubCommands.Where(sc => sc.SubCommands.Count == 0 && CanShow([..chain, sc], context))
					.Select(sc => $"`{sc.Name}`").ToArray();
				var groups = item.SubCommands.Where(sc => sc.SubCommands.Count > 0 && CanShow([..chain, sc], context))
					.Select(sc => $"`{sc.Name}`").ToArray();
				if (subs.Length > 0) lines.Add($"**Subcommands**: {string.Join(", ", subs)}");
				if (groups.Length > 0) lines.Add($"**Command Groups**: {string.Join(", ", groups)}");
			}

			lines.Add($"**Help**: `/help path:{FullPath(chain)}`");
			builder.AddField($"/{FullPath(chain)}", string.Join('\n', lines));
		}

		var pages = (int)Math.Ceiling(items.Count / (double)_pageSize);
		if (pages > 1)
			builder.WithFooter($"Page {page + 1} of {pages}");

		return builder.Build();
	}

	private static Embed BuildCommandInfo(List<ICommand> chain)
	{
		var cmd = chain[^1];
		var builder = new EmbedBuilder()
			.WithTitle($"/{FullPath(chain)}")
			.WithDescription(cmd.Description)
			.WithColor(Color.Purple)
			.AddField("Usage", $"`{Usage(chain)}`");

		var parameters = Parameters(cmd)
			.Select(opt => $"`{opt.Name}`{(opt.IsRequired == true ? string.Empty : " *(optional)*")}: {opt.Description}")
			.ToArray();
		if (parameters.Length > 0)
			builder.AddField("Parameters", string.Join('\n', parameters));

		return builder.Build();
	}

	/// <summary>
	/// Whether the last command in <paramref name="chain"/> should be listed. Its requirements already include any
	/// inherited from the rest of the chain, but hiding applies if anything along the way asks for it.
	/// </summary>
	private bool CanShow(List<ICommand> chain, SocketInteraction context) =>
		_cmd.IsAvailable(chain[^1], context) &&
		(_cmd.AllowedToExecute(chain[^1], context) || !chain.Any(c => c.HideIfCantExecute));

	private static string FullPath(IEnumerable<ICommand> chain) => string.Join(' ', chain.Select(c => c.Name));

	private static string Usage(List<ICommand> chain)
	{
		var args = Parameters(chain[^1]).Select(opt => opt.IsRequired == true ? $"<{opt.Name}>" : $"[{opt.Name}]");
		return $"/{string.Join(' ', chain.Select(c => c.Name).Concat(args))}";
	}

	private static IEnumerable<SlashCommandOptionBuilder> Parameters(ICommand cmd) =>
		cmd.Options.Where(opt =>
			opt.Type != ApplicationCommandOptionType.SubCommand &&
			opt.Type != ApplicationCommandOptionType.SubCommandGroup);
}
