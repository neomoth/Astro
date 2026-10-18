using RTLogging;

namespace Astro.Abstractions;

public interface IInterval
{
	public void Start();
	public void Stop();
	public void Pause();
}

public sealed class Interval(TimeSpan interval, Action callback) : IInterval
{
	private static readonly ISawmill Sawmill = Dependencies.Resolve<ILogManager>().GetSawmill("astro.interval");
	private readonly CancellationTokenSource _token = new();
	private Task? _runningTask;
	private TaskCompletionSource? _pauseTcs;

	public void Start()
	{
		if (_runningTask is not null) return;
		if (_pauseTcs is not null)
		{
			_pauseTcs.TrySetResult();
			_pauseTcs = null;
			return;
		}

		_runningTask = Task.Run(async () =>
		{
			while (!_token.IsCancellationRequested)
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
					Sawmill.Log(LogLevel.Error, e, "Error during interval task.");
				}
			}
		});

	}

	public void Stop()
	{
		_token.Cancel();
		_pauseTcs?.TrySetResult();
		_runningTask = null;
	}

	public void Pause()
	{
		if (_pauseTcs is not null) return;
		_pauseTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	private async Task Wait()
	{
		var remaining = interval;
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		while (remaining.TotalMilliseconds > 0)
		{
			if (_pauseTcs is not null)
			{
				await _pauseTcs.Task;
				stopwatch.Restart();
			}

			try
			{
				var wait = remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
				await Task.Delay(wait, _token.Token);
			}
			catch (TaskCanceledException)
			{
				break;
			}

			remaining = interval - stopwatch.Elapsed;
		}
	}
}
