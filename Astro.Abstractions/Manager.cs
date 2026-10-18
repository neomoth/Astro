using IoC;
using RTLogging;

namespace Astro.Abstractions;

/// <summary>
/// Base for IoC-provided managers. Gives each one its own sawmill and a hook that runs once the dependency graph
/// has been built and every <c>[Dependency]</c> field is set.
/// </summary>
public abstract class Manager : IDependency, IPostInjectInit
{
	[Dependency] private readonly ILogManager _log = null!;

	/// <summary>The name of this manager's sawmill, e.g. <c>astro.commands</c>.</summary>
	protected abstract string SawmillName { get; }
	protected ISawmill Log => field ??= _log.GetSawmill(SawmillName);

	public virtual void PostInject() { }

	public virtual void Initialize() { }
}
