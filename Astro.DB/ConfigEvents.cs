using Astro.DB.Entities;

namespace Astro.DB;

public enum ConfigChangeKind
{
	Created,
	Updated,
	Deleted,
}

/// <summary>
/// A config row that was saved. <see cref="Entity"/> is its new state, or null if it was deleted.
/// </summary>
/// <param name="ChangedProperties">The properties that changed; every property for <see cref="ConfigChangeKind.Created"/>, none for <see cref="ConfigChangeKind.Deleted"/>.</param>
public sealed record ConfigChange(
	Type EntityType,
	ulong Id,
	IConfigEntity? Entity,
	ConfigChangeKind Kind,
	IReadOnlyCollection<string> ChangedProperties)
{
	public bool Changed(string property) => ChangedProperties.Contains(property);
}

/// <summary>
/// Notifies listeners whenever a config row (any <see cref="IConfigEntity"/>) is created, updated or deleted, however it
/// was written: <see cref="AstroDb.Update{T}"/>, <see cref="AstroDb.Run"/>, <see cref="AstroDb.Delete{T}"/>, etc.
/// </summary>
/// <remarks>
/// Handlers run after the change is saved, one at a time. A handler that throws doesn't affect the save or other
/// handlers; it's reported through <see cref="HandlerFailed"/>. Handlers that write the same row they're notified about
/// will be notified again, so guard against loops.
/// </remarks>
/// <example>
/// <code>
/// ConfigEvents.Subscribe&lt;GuildConfig&gt;(async (guild, change) =>
/// {
///     if (change.Changed(nameof(GuildConfig.LoggingChannelId)))
///         await SomethingWith(guild?.LoggingChannelId);
/// });
/// </code>
/// </example>
public static class ConfigEvents
{
	/// <summary>Raised for every config change, of any entity type.</summary>
	public static event Func<ConfigChange, Task>? Changed;

	/// <summary>A <see cref="Changed"/> handler threw.</summary>
	public static event Action<ConfigChange, Exception>? HandlerFailed;

	/// <summary>
	/// Calls <paramref name="handler"/> for changes to <typeparamref name="T"/> rows, with the new state (null when
	/// deleted). Dispose the result to unsubscribe.
	/// </summary>
	public static IDisposable Subscribe<T>(Func<T?, ConfigChange, Task> handler) where T : class, IConfigEntity
	{
		Changed += Filtered;
		return new Subscription(() => Changed -= Filtered);

		Task Filtered(ConfigChange change) =>
			change.EntityType == typeof(T) ? handler(change.Entity as T, change) : Task.CompletedTask;
	}

	/// <summary>
	/// Like <see cref="Subscribe{T}(Func{T, ConfigChange, Task})"/>, for handlers that don't need to await anything.
	/// </summary>
	public static IDisposable Subscribe<T>(Action<T?, ConfigChange> handler) where T : class, IConfigEntity =>
		Subscribe<T>((entity, change) =>
		{
			handler(entity, change);
			return Task.CompletedTask;
		});

	internal static async Task Raise(IEnumerable<ConfigChange> changes)
	{
		foreach (var change in changes)
		{
			foreach (var handler in Changed?.GetInvocationList().Cast<Func<ConfigChange, Task>>() ?? [])
			{
				try
				{
					await handler(change);
				}
				catch (Exception e)
				{
					HandlerFailed?.Invoke(change, e);
				}
			}
		}
	}

	private sealed class Subscription(Action unsubscribe) : IDisposable
	{
		private Action? _unsubscribe = unsubscribe;
		public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
	}
}
