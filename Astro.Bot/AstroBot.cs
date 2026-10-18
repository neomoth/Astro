using Astro.Bot.Behaviors;
using Astro.Bot.Commands;
using Discord;
using Discord.WebSocket;
using Astro.Abstractions;
using IoC;
using RTLogging;
using Astro.Bot.Configuration;
using Astro.Bot.OAuth;
using Astro.Bot.Permissions;
using Astro.DB;

namespace Astro.Bot;

public sealed class AstroBot : IDependency
{
	[Dependency] private readonly ILogManager _log = null!;
	[Dependency] private readonly GlobalConfigManager config = null!;
	[Dependency] private readonly CommandManager commandManager = null!;
	[Dependency] private readonly BehaviorManager behaviorManager = null!;
	[Dependency] private readonly OAuthManager oauthManager = null!;
	public ISawmill Log => field ??= _log.GetSawmill("astro");
	public DiscordSocketClient Client { get; private set; } = null!;

	public async Task Initialize()
	{
		oauthManager.Start();
		Client = new DiscordSocketClient(new DiscordSocketConfig
			{ GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent });
		commandManager.Load();
		var token = config.Registry.Get<string>("Bot.ClientToken");
		if (token is null || string.IsNullOrWhiteSpace(token))
		{
			Log.Fatal("Bot token is empty, please fill it in at {path}", config.FullPath);
			throw new StartupException("Bot token is empty.");
		}

		Client.Log += LogAsync;
		Client.Ready += OnReady;
		Client.SlashCommandExecuted += commandManager.HandleCommand;
		behaviorManager.AttachAll(Client);
		await Client.LoginAsync(TokenType.Bot, token);
		await Client.StartAsync();
	}

	private async Task OnReady()
	{
		await commandManager.InitSlashCommands();
		RandomStatus.Start();
		await RandomStatus.CycleStatus();
		await Client.SetStatusAsync(UserStatus.Online);
		Log.Info("Astro is ready.");
	}

	private async Task LogAsync(Discord.LogMessage message)
	{
		if (message.Exception is TaskCanceledException)
			return;
		var severity = message.Severity switch
		{
			LogSeverity.Critical => LogLevel.Fatal,
			LogSeverity.Error => LogLevel.Error,
			LogSeverity.Warning => LogLevel.Warn,
			LogSeverity.Info => LogLevel.Info,
			LogSeverity.Verbose => LogLevel.Verbose,
			LogSeverity.Debug => LogLevel.Debug,
			_ => LogLevel.Info
		};
		// Discord.Net sources (Gateway, Rest, ...) each get their own sawmill under "discord".
		_log.GetSawmill($"discord.{message.Source.ToLowerInvariant()}")
			.Log(severity, message.Exception, "{MessageMessage}", message.Message);
		await Task.CompletedTask;
	}

	public async Task Shutdown()
	{
		Log.Info("Shutting down...");
		RandomStatus.Stop();
		behaviorManager.DetachAll();
		oauthManager.Stop();
		await Client.SetCustomStatusAsync("Shutting down!");
		await Client.SetStatusAsync(UserStatus.DoNotDisturb);
		await Client.StopAsync();
	}
}
