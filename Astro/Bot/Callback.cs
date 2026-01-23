namespace Astro.Bot;

public class Callback(Delegate action)
{
	private readonly Delegate action = action ?? throw new ArgumentException(null, nameof(action));

	public void Invoke(params object[] args) => action.DynamicInvoke(args);
}

public class Callback<TResult>(Delegate func)
{
	private readonly Delegate func = func ?? throw new ArgumentException(null, nameof(func));
	
	public TResult Invoke(params object[] args) => (TResult)func.DynamicInvoke(args)!;
}