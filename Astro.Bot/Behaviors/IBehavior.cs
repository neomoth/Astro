using Astro.Abstractions;
using Discord.WebSocket;
using JetBrains.Annotations;

namespace Astro.Bot.Behaviors;

public interface IBehavior : IDisposable
{
	public string Name { get; }
	public string Description { get; }
	public bool Enabled { get; }
	public IEnumerable<IConfigOption> CreateConfigOptions();
	public void Attach(DiscordSocketClient client);
	public void Detach();
}

/// <summary>
/// Marks a behavior to be discovered and loaded by <see cref="BehaviorManager"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class BehaviorAttribute : Attribute;
