using Astro.Bot;
using Astro.Bot.Commands;
using Serilog;
using Serilog.Events;

[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(HotReloadHandler))]
namespace Astro;

public class Program
{
	public static AstroBot Bot = default!;
	private readonly TaskCompletionSource shutdown = new(TaskCreationOptions.RunContinuationsAsynchronously);

	public static void Main(string[] args) => new Program().MainAsync().GetAwaiter().GetResult();

	private async Task MainAsync()
	{
		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.Is(GetLogLevel())
			.Enrich.FromLogContext()
			.WriteTo.Console()
			.CreateLogger();
		Log.Verbose("Logger initialized.");
		
		Bot = new AstroBot();
		Console.CancelKeyPress += (sender, e) =>
		{
			e.Cancel = true;
			shutdown.TrySetResult();
		};

		AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
		{
			shutdown.TrySetResult();
		};

		await Bot.Initialize();
		await shutdown.Task;
		await Bot.Shutdown();
	}

	private static LogEventLevel GetLogLevel()
	{
#if DEBUG
		return LogEventLevel.Verbose;
#elif RELEASE
		return LogEventLevel.Information;
#else
		return LogEventLevel.Information;
#endif
	}
}