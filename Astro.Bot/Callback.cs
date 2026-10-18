namespace Astro.Bot;

public sealed class Callback(Delegate action)
{
	private readonly Delegate _action = action ?? throw new ArgumentException(null, nameof(action));

	public void Invoke(params object[] args) => _action.DynamicInvoke(args);
}

public sealed class Callback<TResult>(Delegate func)
{
	private readonly Delegate _func = func ?? throw new ArgumentException(null, nameof(func));

	public TResult Invoke(params object[] args) => (TResult)_func.DynamicInvoke(args)!;
}
