namespace Astro.Bot.Conditions;

public interface ICondition<in T>
{
	public bool Check(T context);
}

/// <summary>
/// Helpers for building and composing conditions inline.
/// </summary>
public static class Condition
{
	public static ICondition<T> From<T>(Func<T, bool> predicate) => new PredicateCondition<T>(predicate);
	public static ICondition<T> All<T>(params ICondition<T>[] conditions) => From<T>(ctx => conditions.All(c => c.Check(ctx)));
	public static ICondition<T> Any<T>(params ICondition<T>[] conditions) => From<T>(ctx => conditions.Any(c => c.Check(ctx)));

	public static ICondition<T> And<T>(this ICondition<T> left, ICondition<T> right) => All(left, right);
	public static ICondition<T> Or<T>(this ICondition<T> left, ICondition<T> right) => Any(left, right);
	public static ICondition<T> Not<T>(this ICondition<T> condition) => From<T>(ctx => !condition.Check(ctx));

	private sealed class PredicateCondition<T>(Func<T, bool> predicate) : ICondition<T>
	{
		public bool Check(T context) => predicate(context);
	}
}
