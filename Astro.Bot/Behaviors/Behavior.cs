using System.Reflection;
using Astro.Abstractions;
using Astro.Bot.Configuration;
using Discord.WebSocket;
using IoC;
using RTLogging;

namespace Astro.Bot.Behaviors;

/// <summary>
/// Base for all behaviors. Every behavior gets a <c>Behaviors.{Name}.Enabled</c> config option, and can add its own
/// options under the same prefix via <see cref="DefineOptions"/>.
/// </summary>
/// <remarks>
/// Behaviors only run in servers the bot has joined, unless marked with <see cref="AvailableInAttribute"/> (e.g.
/// <c>[AvailableIn(CommandLocation.Server | CommandLocation.BotDm)]</c>). Discord never sends the bot events from
/// places it's only reachable through a user install, so <see cref="CommandLocation.OtherServers"/> never applies.
/// </remarks>
public abstract class Behavior : IBehavior
{
	[Dependency] private readonly ILogManager _log = null!;
	protected ISawmill Log => field ??= _log.GetSawmill($"astro.behaviors.{Name.ToLowerInvariant()}");

	public abstract string Name { get; }
	public abstract string Description { get; }
	protected virtual bool EnabledByDefault => true;
	public bool Enabled => GlobalConfig.Get<bool>(OptionPath("Enabled"));

	/// <summary>Where this behavior runs: its <see cref="AvailableInAttribute"/>, or servers the bot is in.</summary>
	public CommandLocation Locations =>
		field is CommandLocation.None
			? field = GetType().GetCustomAttribute<AvailableInAttribute>()?.Locations ?? CommandLocation.Server
			: field;

	/// <summary>Whether this behavior should act in <paramref name="channel"/>. Check this in non-message handlers.</summary>
	protected bool RunsIn(ISocketMessageChannel channel) => (Locations & LocationOf(channel)) != 0;

	protected static CommandLocation LocationOf(ISocketMessageChannel channel) => channel switch
	{
		SocketGuildChannel => CommandLocation.Server,
		SocketDMChannel => CommandLocation.BotDm,
		SocketGroupChannel => CommandLocation.PrivateChannels,
		_ => CommandLocation.None,
	};

	protected DiscordSocketClient? Client { get; private set; }

	public IEnumerable<IConfigOption> CreateConfigOptions() =>
		DefineOptions().Prepend(Option("Enabled", EnabledByDefault));

	protected virtual IEnumerable<IConfigOption> DefineOptions() => [];

	protected string OptionPath(string key) => $"Behaviors.{Name}.{key}";
	protected ConfigOption<T> Option<T>(string key, T defaultValue) => new(OptionPath(key), defaultValue);
	protected T? Get<T>(string key) => GlobalConfig.Get<T>(OptionPath(key));

	public void Attach(DiscordSocketClient client)
	{
		if (Client is not null)
			throw new InvalidOperationException($"Behavior {Name} is already attached.");
		Client = client;
		OnAttach(client);
	}

	public void Detach()
	{
		if (Client is null) return;
		OnDetach(Client);
		Client = null;
	}

	/// <summary>Subscribe to client events here. Wrap handlers with <see cref="Guard{T}"/>.</summary>
	protected abstract void OnAttach(DiscordSocketClient client);

	/// <summary>Unsubscribe the same handler instances that were subscribed in <see cref="OnAttach"/>.</summary>
	protected abstract void OnDetach(DiscordSocketClient client);

	/// <summary>
	/// Wraps an event handler so it only runs while the behavior is enabled, runs off the gateway thread, and
	/// logs exceptions instead of letting them escape. Handlers may therefore run concurrently.
	/// </summary>
	protected Func<T, Task> Guard<T>(Func<T, Task> handler) => arg =>
	{
		if (!Enabled) return Task.CompletedTask;
		_ = Task.Run(async () =>
		{
			try
			{
				await handler(arg);
			}
			catch (Exception e)
			{
				Log.Log(LogLevel.Error, e, "Threw while handling {event}.", typeof(T).Name);
			}
		});
		return Task.CompletedTask;
	};

	public virtual void Dispose()
	{
		Detach();
		GC.SuppressFinalize(this);
	}
}
