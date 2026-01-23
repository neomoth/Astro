using Astro.Bot.Conditions;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Behaviors;

public sealed class RepeatMessageBehavior : IBehavior
{
	public List<ICondition> Conditions { get; } = [];
	private readonly Dictionary<ISocketMessageChannel, List<IUserMessage>> messageCache = [];

	public RepeatMessageBehavior()
	{
		AstroBot.Client.MessageReceived += OnMessageReceived;
	}

	public void Destroy()
	{
		AstroBot.Client.MessageReceived -= OnMessageReceived;
	}

	private async Task OnMessageReceived(SocketMessage message)
	{
		await TryExecute(message);
	}
	
	public async Task TryExecute(params object[] args)
	{
		var message = args.OfType<SocketUserMessage>().FirstOrDefault();
		if (message == null) return;
		if (string.IsNullOrWhiteSpace(message.Content)) return;
		if (message.Author.IsBot) return;
		if (!messageCache.ContainsKey(message.Channel)) messageCache[message.Channel] = [];
		var channelCache = messageCache[message.Channel];
		channelCache.Add(message);

		// Keep only the last 3 messages
		if (channelCache.Count > 3)
			channelCache.RemoveAt(0);
		if (channelCache.Count < 3)
			return;

		// Pass the last 3 messages as object[]
		var argsForCondition = channelCache.Cast<object>().ToArray();

		if (!Conditions.All(c => c.Check(argsForCondition))) return;
		await message.Channel.SendMessageAsync(messageCache[message.Channel].Last().Content);
		channelCache.Clear();
	}
}