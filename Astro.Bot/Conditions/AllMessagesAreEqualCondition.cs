using Discord.WebSocket;

namespace Astro.Bot.Conditions;

public sealed class AllMessagesAreEqualCondition : ICondition<IReadOnlyList<SocketUserMessage>>
{
	public bool Check(IReadOnlyList<SocketUserMessage> messages)
	{
		if (messages.Count == 0) return false;

		var firstContent = messages[0].Content;
		return messages.All(m => string.Equals(m.Content, firstContent, StringComparison.OrdinalIgnoreCase));
	}
}
