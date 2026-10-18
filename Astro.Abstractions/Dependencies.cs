using IoC;

namespace Astro.Abstractions;

/// <summary>
/// Thread-agnostic access to the bot's dependency collection.
/// </summary>
/// <remarks>
/// <see cref="IoCManager"/> is thread-local, and most of the bot runs on thread pool threads (async continuations,
/// Discord.Net events), where it has no context. Prefer <c>[Dependency]</c> fields on anything instantiated through
/// <see cref="Inject{T}"/>; fall back to <see cref="Resolve{T}"/> only in static classes, which can't be injected.
/// </remarks>
public static class Dependencies
{
	private static IDependencyCollection? _collection;

	public static IDependencyCollection Collection =>
		_collection ?? throw new InvalidOperationException("Dependencies have not been initialized.");

	/// <summary>Creates the collection and makes it the <see cref="IoCManager"/> context for the calling thread.</summary>
	public static IDependencyCollection Initialize()
	{
		if (_collection is not null)
			throw new InvalidOperationException("Dependencies have already been initialized.");
		_collection = IoCManager.InitThread();
		return _collection;
	}

	public static T Resolve<T>() => Collection.Resolve<T>();

	public static T Inject<T>(T obj) where T : notnull
	{
		Collection.InjectDependencies(obj);
		return obj;
	}

	/// <summary>Disposes every registered service. Nothing may be resolved afterward.</summary>
	public static void Shutdown()
	{
		_collection?.Clear();
		_collection = null;
	}
}
