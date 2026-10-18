using Astro.Abstractions;
using Astro.Bot.Configuration;
using IoC;
using RTLogging;

namespace Astro.Bot;

public static class RandomStatus
{
	private static readonly ISawmill Sawmill = Dependencies.Resolve<ILogManager>().GetSawmill("astro.status");
	private static string? _currentStatus;
	public static event EventHandler? StatusUpdated;
	private static readonly Random Random = new();
	private static readonly Interval Interval;

	static RandomStatus() => Interval = new Interval(TimeSpan.FromMinutes(GlobalConfig.Get<double>("Bot.Status.StatusUpdateInterval")),
		async void () =>
		{
			try
			{
				await CycleStatus();
			}
			catch (Exception e)
			{
				Sawmill.Log(LogLevel.Error, e, "Failed to cycle status.");
			}
		});

	public static async Task CycleStatus()
	{
		var statuses = GlobalConfig.Get<string[]>("Bot.Status.Statuses");
		_currentStatus = statuses?[Random.Next(statuses.Length)];
		await Dependencies.Resolve<AstroBot>().Client.SetCustomStatusAsync(
			$"{GlobalConfig.Get<string>("Bot.Status.StatusPrefix")}{(!string.IsNullOrEmpty(_currentStatus) ? $" | {_currentStatus}" : string.Empty)}");
		StatusUpdated?.Invoke(null, new RandomStatusUpdatedEventArgs(_currentStatus));
	}

	public static void Start() => Interval.Start();
	public static void Pause() => Interval.Pause();
	public static void Stop() => Interval.Stop();
}

public sealed class RandomStatusUpdatedEventArgs(string? status) : EventArgs
{
	public string? NewStatus { get; } = status;
}
