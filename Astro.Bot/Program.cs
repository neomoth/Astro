using System.Reflection;
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

	public static int Main(string[] args)
	{
		for (var i = 0; i < args.Length; i++)
		{
			switch (args[i])
			{
				case "--home" when i + 1 < args.Length:
					ResourcePaths.UseHome(args[++i]);
					break;
				case "--paths":
					Console.WriteLine($"""
						Mode:   {ResourcePaths.Mode}
						Config: {ResourcePaths.Config}
						Data:   {ResourcePaths.Data}
						Logs:   {ResourcePaths.Logs}
						Keys:   {ResourcePaths.Keys}
						""");
					return 0;
				case "--version":
					Console.WriteLine(Version);
					return 0;
				case "--help" or "-h":
					Console.WriteLine($"""
						Astro {Version}

						Usage: astro [--home <dir>] [--paths]

						  --home <dir>  Keep config, data and logs in <dir> instead of the default locations.
						                Same as setting ASTRO_HOME.
						  --paths       Print where config, data and logs are kept, and exit.
						  --version     Print the version and exit.
						""");
					return 0;
				default:
					Console.Error.WriteLine($"Unknown argument '{args[i]}'. Run with --help for usage.");
					return 2;
			}
		}

		return new Program().MainAsync().GetAwaiter().GetResult();
	}

	private static string Version =>
		typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
		?? "unknown";

	private async Task<int> MainAsync()
	{
		var deps = Dependencies.Initialize();
		deps.RegisterInstance<ILogManager>(CreateLogManager());
		AstroIoC.Register(deps);
		deps.BuildGraph();
		deps.Resolve<ILogManager>().GetSawmill("astro").Info("Astro {version:l} ({mode:l} paths). Config: {config}, data: {data}, logs: {logs}",
			Version, ResourcePaths.Mode, ResourcePaths.Config, ResourcePaths.Data, ResourcePaths.Logs);

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

		try
		{
			await _bot.Initialize();
		}
		catch (StartupException)
		{
			// Already logged where it was thrown; just exit without a stack trace.
			Dependencies.Shutdown();
			return 1;
		}

		await _shutdown.Task;
		await _bot.Shutdown();
		Dependencies.Shutdown(); // disposes LogManager, flushing and closing the log handlers
		return 0;
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
