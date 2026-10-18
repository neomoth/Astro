using Astro.Bot.Conditions;
using Discord.WebSocket;

namespace Astro.Bot.Behaviors;

/// <summary>
/// Base for behaviors that react to incoming user messages.
/// </summary>
public abstract class MessageBehavior : Behavior
{
	private Func<SocketMessage, Task>? _handler;

	protected virtual bool IgnoreBots => true;
	protected virtual bool IgnoreEmpty => true;

	/// <summary>Every filter must pass for a message to reach <see cref="OnMessage"/>.</summary>
	public List<ICondition<SocketUserMessage>> Filters { get; } = [];

	protected sealed override void OnAttach(DiscordSocketClient client)
	{
		_handler = Guard<SocketMessage>(HandleMessage);
		client.MessageReceived += _handler;
	}

	protected sealed override void OnDetach(DiscordSocketClient client)
	{
		if (_handler is not null) client.MessageReceived -= _handler;
		_handler = null;
	}

	private Task HandleMessage(SocketMessage raw)
	{
		if (raw is not SocketUserMessage message) return Task.CompletedTask;
		if (!RunsIn(message.Channel)) return Task.CompletedTask;
		if (IgnoreBots && message.Author.IsBot) return Task.CompletedTask;
		if (IgnoreEmpty && string.IsNullOrWhiteSpace(message.Content)) return Task.CompletedTask;
		if (!Filters.All(f => f.Check(message))) return Task.CompletedTask;
		return OnMessage(message);
	}

	protected abstract Task OnMessage(SocketUserMessage message);
}
