using System.Collections.Concurrent;
using Astro.Abstractions;
using Astro.Bot.Conditions;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Behaviors;

[Behavior]
public sealed class RepeatMessageBehavior : MessageBehavior
{
	public override string Name => "RepeatMessage";
	public override string Description => "Joins in when the same message is sent several times in a row.";

	/// <summary>Checked against the last <c>MessageCount</c> messages in a channel.</summary>
	public List<ICondition<IReadOnlyList<SocketUserMessage>>> Conditions { get; } =
	[
		new AllMessagesAreEqualCondition(),
		new AllMessagesAreFromDifferentUsersCondition()
	];

	private readonly ConcurrentDictionary<ulong, Queue<SocketUserMessage>> _history = [];

	protected override IEnumerable<IConfigOption> DefineOptions()
	{
		yield return Option("MessageCount", 3);
	}

	protected override async Task OnMessage(SocketUserMessage message)
	{
		var count = Math.Max(2, Get<int>("MessageCount"));
		var queue = _history.GetOrAdd(message.Channel.Id, _ => new Queue<SocketUserMessage>());
		SocketUserMessage[] window;

		lock (queue)
		{
			queue.Enqueue(message);
			while (queue.Count > count)
				queue.Dequeue();
			if (queue.Count < count)
				return;

			window = queue.ToArray();
			if (!Conditions.All(c => c.Check(window)))
				return;
			queue.Clear();
		}

		await message.Channel.SendMessageAsync(window[^1].Content, allowedMentions: AllowedMentions.None);
	}

	public override void Dispose()
	{
		base.Dispose();
		_history.Clear();
	}
}
