using Astro.Bot.Conditions;

namespace Astro.Bot.Behaviors;

public interface IBehavior
{
	public List<ICondition> Conditions { get; }
	public Task TryExecute(params object[] args);
}