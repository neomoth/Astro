using Astro.Bot.Behaviors;
using Astro.Bot.Commands;
using Astro.Abstractions;
using Astro.Bot.Configuration;
using Astro.DB;
using RTLogging;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadHandler))]
namespace Astro.Bot;

public sealed class Program
{
	private static AstroBot _bot = null!;
	private readonly TaskCompletionSource _shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

	private async Task MainAsync()
	{
		var deps = Dependencies.Initialize();
		deps.RegisterInstance<ILogManager>(CreateLogManager());
		AstroIoC.Register(deps);
		deps.BuildGraph();

		// Storage first, so managers can rely on the config and database in Initialize.
		deps.Resolve<BehaviorManager>().Load(); // before LoadConfig so saved behavior options are applied
		deps.Resolve<GlobalConfigManager>().LoadConfig();
		await AstroDb.Initialize();
		deps.Resolve<ILogManager>().GetSawmill("astro").Verbose("Database initialized at {path}", AstroDb.FullPath);
		var configEventsLog = deps.Resolve<ILogManager>().GetSawmill("astro.db.events");
		ConfigEvents.HandlerFailed += (change, e) => configEventsLog.Log(LogLevel.Error, e,
			"A ConfigEvents handler threw for {kind} {type} {id}.", change.Kind, change.EntityType.Name, change.Id);

		// In AstroIoC registration order, so a manager can rely on ones registered before it.
		foreach (var manager in AstroIoC.Registered.Select(deps.ResolveType).OfType<Manager>())
			manager.Initialize();

		_bot = deps.Resolve<AstroBot>();
		Console.CancelKeyPress += (sender, e) =>
		{
			e.Cancel = true;
			_shutdown.TrySetResult();
		};

		AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
		{
			_shutdown.TrySetResult();
		};

		await _bot.Initialize();
		await _shutdown.Task;
		await _bot.Shutdown();
		Dependencies.Shutdown(); // disposes LogManager, flushing and closing the log handlers
	}

	private static LogManager CreateLogManager()
	{
		var log = new LogManager();
		log.RootSawmill.Level = GetLogLevel();
		log.RootSawmill.AddHandler(new ConsoleLogHandler());
		var logFile = Path.Combine(ResourcePaths.Logs, $"astro-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
		log.RootSawmill.AddHandler(new FileLogHandler(logFile));
		log.RootSawmill.Verbose("Logger initialized. Writing logs to {path}", logFile);
		return log;
	}

	private static LogLevel GetLogLevel()
	{
#if DEBUG
		return LogLevel.Verbose;
#elif RELEASE
		return LogLevel.Info;
#else
		return LogLevel.Info;
#endif
	}
}
