using Astro.Bot.Behaviors;
using Astro.Bot.Commands;
using Astro.Bot.Conditions;
using Discord;
using Discord.WebSocket;
using Serilog;
using Serilog.Events;
using Astro.Bot.Configuration;

namespace Astro.Bot;

public sealed class AstroBot
{
	public static DiscordSocketClient Client { get; private set; } = default!;
	public static List<IBehavior> LoadedBehaviors { get; } = []; // TODO: make a proper manager for this shit.

	public static GlobalConfigManager Config = default!;
	
	public async Task Initialize()
	{
		Config = new GlobalConfigManager().LoadConfig();
		Client = new DiscordSocketClient(new DiscordSocketConfig{GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent});
		CommandManager.Load();
		var token = Config.Registry.Get<string>("Bot.ClientToken");
		if (token is null || string.IsNullOrWhiteSpace(token))
		{
			Log.Fatal("Bot token is empty, please fill it in at {path}", Config.FullPath);
			await Task.FromException(new Exception("Bot token is empty"));
			return;
		}
		Client.Log += LogAsync;
		Client.Ready += OnReady;
		Client.SlashCommandExecuted += CommandManager.HandleCommand;
		LoadedBehaviors.Add(new RepeatMessageBehavior {Conditions = { new AllMessagesAreEqualCondition(), new AllMessagesAreFromDifferentUsersCondition() }});
		await Client.LoginAsync(TokenType.Bot, token);
		await Client.StartAsync();
	}

	private async Task OnReady()
	{
		await CommandManager.InitSlashCommands();
		RandomStatus.Start();
		await RandomStatus.CycleStatus();
		await Client.SetStatusAsync(UserStatus.Online);
		Log.Information("Astro is ready.");
	}

	private async Task LogAsync(LogMessage message)
	{
		if (message.Exception is TaskCanceledException)
			return;
		var severity = message.Severity switch
		{
			LogSeverity.Critical => LogEventLevel.Fatal,
			LogSeverity.Error => LogEventLevel.Error,
			LogSeverity.Warning => LogEventLevel.Warning,
			LogSeverity.Info => LogEventLevel.Information,
			LogSeverity.Verbose => LogEventLevel.Verbose,
			LogSeverity.Debug => LogEventLevel.Debug,
			_ => LogEventLevel.Information
		};
		Log.Write(severity, message.Exception, "[{MessageSource}] {MessageMessage}", message.Source, message.Message);
		await Task.CompletedTask;
	}

	public async Task Shutdown()
	{
		Log.Information("Shutting down...");
		RandomStatus.Stop();
		await Client.SetCustomStatusAsync("Shutting down!");
		await Client.SetStatusAsync(UserStatus.DoNotDisturb);
		await Client.StopAsync();
	}
}