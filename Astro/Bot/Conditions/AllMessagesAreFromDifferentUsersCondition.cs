using Discord.WebSocket;

namespace Astro.Bot.Conditions;

public sealed class AllMessagesAreFromDifferentUsersCondition : ICondition
{
	public bool Check(params object[] args)
	{
		var messages = args.OfType<SocketUserMessage>().ToArray();
		if (messages.Length == 0) return false;

		var seenUsers = new List<ulong>();
		foreach (var message in messages)
		{
			if (seenUsers.Contains(message.Author.Id)) return false;
			seenUsers.Add(message.Author.Id);
		}

		return true;
	}
}