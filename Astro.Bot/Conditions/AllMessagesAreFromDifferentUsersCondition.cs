using Discord.WebSocket;

namespace Astro.Bot.Conditions;

public sealed class AllMessagesAreFromDifferentUsersCondition : ICondition<IReadOnlyList<SocketUserMessage>>
{
	public bool Check(IReadOnlyList<SocketUserMessage> messages)
	{
		if (messages.Count == 0) return false;

		var seenUsers = new HashSet<ulong>();
		return messages.All(m => seenUsers.Add(m.Author.Id));
	}
}
