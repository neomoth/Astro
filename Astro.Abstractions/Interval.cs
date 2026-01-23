using Serilog;

namespace Astro.Abstractions;

public interface IInterval
{
	public void Start();
	public void Stop();
	public void Pause();
}

public sealed class Interval(TimeSpan interval, Action callback) : IInterval
{
	private readonly CancellationTokenSource token = new();
	private Task? runningTask;
	private TaskCompletionSource? pauseTcs;

	public void Start()
	{
		if (runningTask is not null) return;
		if (pauseTcs is not null)
		{
			pauseTcs.TrySetResult();
			pauseTcs = null;
			return;
		}

		runningTask = Task.Run(async () =>
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					callback.Invoke();
					await Wait();
				}
				catch (TaskCanceledException)
				{
					break;
				}
				catch (Exception e)
				{
					Log.Error("Error during interval task: {error}", e.Message);
				}
			}
		});

	}

	public void Stop()
	{
		token.Cancel();
		pauseTcs?.TrySetResult();
		runningTask = null;
	}

	public void Pause()
	{
		if (pauseTcs is not null) return;
		pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private async Task Wait()
	{
		var remaining = interval;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		while (remaining.TotalMilliseconds > 0)
		{
			if (pauseTcs is not null)
			{
				await pauseTcs.Task;
				stopwatch.Restart();
			}

			try
			{
				var wait = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
				await Task.Delay(wait, token.Token);
			}
			catch (TaskCanceledException)
			{
				break;
			}

			remaining = interval - stopwatch.Elapsed;
		}
	}
}