namespace Astro.Bot.Conditions;

public interface ICondition
{
	public bool Check(params object[] args);
}