using Astro.Abstractions;
using Astro.Bot.Configuration;
using Serilog;

namespace Astro.Bot;

public static class RandomStatus
{
	public static string? CurrentStatus;
	public static event EventHandler? StatusUpdated;
	private static readonly Random Random = new();
	private static Interval interval;

	static RandomStatus() => interval = new Interval(TimeSpan.FromMinutes(GlobalConfig.Get<double>("Bot.Status.StatusUpdateInterval")),
		async void () =>
		{
			try
			{
				await CycleStatus();
			}
			catch (Exception e)
			{
				Log.Error("{error}", e.Message);
			}
		});

	public static async Task CycleStatus()
	{
		var statuses = GlobalConfig.Get<string[]>("Bot.Status.Statuses");
		CurrentStatus = statuses?[Random.Next(statuses.Length)];
		await AstroBot.Client.SetCustomStatusAsync($"{GlobalConfig.Get<string>("Bot.Status.StatusPrefix")}{(!string.IsNullOrEmpty(CurrentStatus) ? $" | {CurrentStatus}" : string.Empty)}");
		StatusUpdated?.Invoke(null, new RandomStatusUpdatedEventArgs(CurrentStatus));
	}

	public static void Start() => interval.Start();
	public static void Pause() => interval.Pause();
	public static void Stop() => interval.Stop();
}

public sealed class RandomStatusUpdatedEventArgs(string? status) : EventArgs
{
	public string? NewStatus { get; } = status;
}