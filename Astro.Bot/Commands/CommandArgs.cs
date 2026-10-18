using Astro.Abstractions;
using Discord;
using Discord.WebSocket;

namespace Astro.Bot.Commands;

public sealed class CommandArgs : ICommandArgs
{
	private readonly Dictionary<string, object?> _values;
	private CommandArgs(Dictionary<string, object?> values) => _values = values;
	public IReadOnlyDictionary<string, object?> Values => _values;

	public static CommandArgs Create(SocketSlashCommand command)
	{
		var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
		foreach (var option in command.Data.Options) Flatten(option, dict);
		return new CommandArgs(dict);
	}

	private static void Flatten(SocketSlashCommandDataOption option, Dictionary<string, object?> dict)
	{
		if(option.Type is ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup)
			foreach (var child in option.Options)
				Flatten(child, dict);
		dict[option.Name] = option.Value;
	}

	public bool Has(string name) => _values.ContainsKey(name);

	public T? Get<T>(string name)
	{
		if (!_values.TryGetValue(name, out var value)) return default;
		if (value is null) return default;
		return (T)ConvertValue(value, typeof(T));
	}

	public T GetRequired<T>(string name)
	{
		if (!_values.TryGetValue(name, out var value) || value is null)
			throw new ArgumentException($"Missing required parameter {name}");

		return (T)ConvertValue(value, typeof(T));
	}

	private static object ConvertValue(object value, Type type)
	{
		// Users, channels, roles, attachments etc. arrive as Discord objects that already are the requested type.
		if (type.IsInstanceOfType(value)) return value;
		var nullable = Nullable.GetUnderlyingType(type);
		if (nullable is not null) return ConvertValue(value, nullable);
		return type.IsEnum ? Enum.Parse(type, value.ToString()!, true) : Convert.ChangeType(value, type);
	}
}
