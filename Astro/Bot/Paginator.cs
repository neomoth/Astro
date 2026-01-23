using Discord;
using Discord.WebSocket;

namespace Astro.Bot;

public class Paginator
{
	private static readonly Dictionary<ulong, (IUserMessage, CancellationTokenSource)> Tokens = new();
	private readonly Callback<Embed> embedCallback;
	private readonly int pageSize;
	private readonly int totalEntries;
	private int btnId;

	public Paginator(Callback<Embed> embedCallback, IUserMessage message, int pageSize, int totalEntries)
	{
		this.embedCallback = embedCallback;
		this.pageSize = pageSize;
		this.totalEntries = totalEntries;
		if (totalEntries <= pageSize) return;
		StartTimeout(message);
	}
	
	public virtual async Task OnButtonExecuted(SocketMessageComponent component)
	{
		var parts = component.Data.CustomId.Split(':');
		if (parts is not [_, _, "page", _]) return;
		ulong id = ulong.Parse(parts[0]);
		int page = int.Parse(parts[3]);
		if (id != component.Message.Id) return;
		if (component.Message.InteractionMetadata.UserId != component.User.Id) return;
		await component.UpdateAsync(msg =>
		{
			msg.Embed = embedCallback.Invoke(page, component);
			msg.Components = BuildComponents(page, id);
		});
		StartTimeout(component.Message);
		await Task.CompletedTask;
	}

	public virtual MessageComponent? BuildComponents(int page, ulong id, bool timedOut = false)
	{
		int start = page * pageSize;
		var atEnd = start >= totalEntries-1 || timedOut;
		var atStart = start == 0 || timedOut;
		if (totalEntries <= pageSize) return null;
		return new ComponentBuilder()
			.WithButton(customId: $"{id}:{btnId++}:page:0", emote: new Emoji("\u23ea"), disabled: atStart)
			.WithButton(customId: $"{id}:{btnId++}:page:{page-1}", emote: new Emoji("\u2b05"), disabled: atStart)
			.WithButton(customId: $"{id}:{btnId++}:page:{page+1}", emote: new Emoji("\u27a1"), disabled: atEnd)
			.WithButton(customId: $"{id}:{btnId++}:page:{Math.Ceiling(totalEntries/(double)pageSize)-1}", emote: new Emoji("\u23e9"), disabled: atEnd)
			.Build();
	}

	private void StartTimeout(IUserMessage message, int timeout = 900)
	{
		if (Tokens.TryGetValue(message.Id, out var tuple))
			tuple.Item2.Cancel();
		
		var cts = new CancellationTokenSource(timeout);
		Tokens[message.Id] = (message, cts);

		_ = Task.Run(async () =>
		{
			try
			{
				await Task.Delay(TimeSpan.FromSeconds(timeout), cts.Token);

				await message.ModifyAsync(msg =>
				{
					msg.Content = "-# *This interaction has expired.*";
					msg.Embed = null;
					msg.Components = BuildComponents(0, 0, true);
				});

				Tokens.Remove(message.Id);
			}
			catch (TaskCanceledException) {}
		}, cts.Token);
	}
}