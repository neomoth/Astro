using Discord.WebSocket;

namespace Astro.Bot.Conditions;

public sealed class AllMessagesAreEqualCondition : ICondition
{
    public bool Check(params object[] args)
    {
        var messages = args.OfType<SocketUserMessage>().ToArray();
        if (messages.Length == 0) return false;

        var firstContent = messages[0].Content;
        return messages.All(m => m.Content.ToLower().Equals(firstContent.ToLower()));
    }
}