using System.Collections;
using System.Globalization;
using System.Text.Json;
using Astro.Abstractions;

namespace Astro.Bot.Configuration;

public sealed class ConfigOption<T>(string name, T defaultValue = default!) : IConfigOption<T>
{
	public void Set(T value) => Value = value;

	public string Name { get; } = name;
	public object? Value { get; private set; } = defaultValue;
	public object? DefaultValue => defaultValue;
	public Type Type { get; } = typeof(T);
	public bool TrySet(object? value)
	{
		try
		{
			if (value is T t)
			{
				Set(t);
				return true;
			}

			var converted = ConvertValue(value, typeof(T));
			Set((T)converted!);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static object? ConvertValue(object? value, Type type)
	{
		if (value is null) return null;
		if (type.IsInstanceOfType(value)) return value;
		if (value is JsonElement e) return JsonSerializer.Deserialize(e.GetRawText(), type);
		return Convert.ChangeType(value, type);
	}
}

public static class ConfigOption
{
	public static string PrettyString(object? value)
	{
		switch (value)
		{
			case null:
				return "null";
			case string s:
				return $"\"{s}\"";
		}

		if (value is IEnumerable enumerable and not string)
		{
			var items = (from object? item in enumerable select PrettyString(item)).ToList();
			return $"[\n\t{string.Join(",\n\t", items)}\n]";
		}

		if (value.GetType().IsEnum) return value.ToString()!;
		return Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.ToString()!;
	}
}