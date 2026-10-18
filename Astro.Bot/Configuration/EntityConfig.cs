using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Astro.Abstractions;
using Astro.Bot.Permissions;
using Astro.DB.Entities;
using Discord.WebSocket;

namespace Astro.Bot.Configuration;

/// <summary>
/// Which database row a <c>/config</c> scope edits, e.g. the current server's <see cref="GuildConfig"/>.
/// </summary>
public interface IConfigScope<TEntity> where TEntity : class, IConfigEntity, new()
{
	/// <summary>Used in command descriptions and replies, e.g. "this server's".</summary>
	static abstract string Possessive { get; }

	/// <summary>The row ID to edit for this interaction, or null if there isn't one (e.g. no server).</summary>
	static abstract ulong? GetId(SocketInteraction interaction);
}

public sealed class ServerConfigScope : IConfigScope<GuildConfig>
{
	public static string Possessive => "this server's";
	public static ulong? GetId(SocketInteraction interaction) => interaction.GuildId;
}

public sealed class UserConfigScope : IConfigScope<UserConfig>
{
	public static string Possessive => "your";
	public static ulong? GetId(SocketInteraction interaction) => interaction.User.Id;
}

/// <summary>
/// Reflection over a config entity's properties, for editing them through <c>/config</c>. Every public read/write
/// property except <see cref="IConfigEntity.Id"/> is an option; see <see cref="ConfigRestrictedAttribute"/> for locking
/// ones down and <see cref="DescriptionAttribute"/> for describing them.
/// </summary>
public static class EntityConfig<TEntity> where TEntity : class, IConfigEntity, new()
{
	public static IReadOnlyList<PropertyInfo> Options { get; } =
	[
		.. typeof(TEntity)
			.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(p => p is { CanRead: true, CanWrite: true } && p.Name != nameof(IConfigEntity.Id))
			.OrderBy(p => p.Name)
	];

	/// <summary>Options someone could edit through <c>/config</c> (with the right flags).</summary>
	public static IEnumerable<PropertyInfo> EditableOptions =>
		Options.Where(p => p.GetCustomAttribute<ConfigRestrictedAttribute>() is not { Required: null });

	public static IEnumerable<PropertyInfo> ListOptions => EditableOptions.Where(p => ListElementType(p) is not null);

	public static PropertyInfo? Find(string key) =>
		Options.FirstOrDefault(p => p.Name.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>The option's initial value on a new row. A fresh instance each call, so lists aren't shared.</summary>
	public static object? DefaultValue(PropertyInfo option) => option.GetValue(new TEntity());

	/// <summary>Whether <paramref name="userId"/> may edit <paramref name="option"/>; if not, why.</summary>
	public static bool CanEdit(PropertyInfo option, ulong userId, UserPermissionManager permissions, out string reason)
	{
		reason = string.Empty;
		switch (option.GetCustomAttribute<ConfigRestrictedAttribute>())
		{
			case null:
				return true;
			case { Required: null }:
				reason = $"`{option.Name}` can't be changed through /config.";
				return false;
			case { Required: { } required } when !permissions.Has(userId, required):
				reason = $"Changing `{option.Name}` requires the {required} global permission.";
				return false;
			default:
				return true;
		}
	}

	/// <summary>The element type if <paramref name="option"/> is a list, else null.</summary>
	public static Type? ListElementType(PropertyInfo option) =>
		option.PropertyType.IsGenericType && option.PropertyType.GetGenericTypeDefinition() == typeof(List<>)
			? option.PropertyType.GetGenericArguments()[0]
			: null;

	public static string Describe(PropertyInfo option, object? value, ulong userId, UserPermissionManager permissions)
	{
		var line = $"{option.Name}: {Format(value)}";
		if (!CanEdit(option, userId, permissions, out _)) line += "  // locked";
		return option.GetCustomAttribute<DescriptionAttribute>() is { } description
			? $"// {description.Description}\n{line}"
			: line;
	}

	public static string Format(object? value) => value switch
	{
		null => "null",
		string s => $"\"{s}\"",
		IEnumerable list => $"[{string.Join(", ", list.Cast<object?>().Select(Format))}]",
		IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? "null",
	};

	/// <summary>
	/// Parses user input for a value of <paramref name="type"/>. Accepts Discord mentions for IDs (<c>&lt;#123&gt;</c>,
	/// <c>&lt;@123&gt;</c>, <c>&lt;@&amp;123&gt;</c>), <c>null</c>/<c>none</c> for nullable values, <c>|</c> or <c>,</c>
	/// between flag enum values, and comma-separated items for lists.
	/// </summary>
	/// <exception cref="FormatException">The input isn't a valid <paramref name="type"/>.</exception>
	public static object? Parse(string input, Type type)
	{
		input = input.Trim();
		var underlying = Nullable.GetUnderlyingType(type);
		if (underlying is not null || !type.IsValueType)
		{
			if (input.Equals("null", StringComparison.OrdinalIgnoreCase) || input.Equals("none", StringComparison.OrdinalIgnoreCase))
				return null;
		}

		type = underlying ?? type;

		if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
		{
			var elementType = type.GetGenericArguments()[0];
			var list = (IList)Activator.CreateInstance(type)!;
			foreach (var item in input.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				list.Add(Parse(item, elementType));
			return list;
		}

		try
		{
			if (type == typeof(string)) return input;
			if (type == typeof(bool))
				return input.ToLowerInvariant() switch
				{
					"true" or "yes" or "on" or "1" => true,
					"false" or "no" or "off" or "0" => false,
					_ => throw new FormatException(),
				};
			if (type.IsEnum)
				return Enum.Parse(type, input.Replace('|', ','), ignoreCase: true);
			if (type == typeof(ulong) || type == typeof(long))
				input = StripMention(input);
			return Convert.ChangeType(input, type, CultureInfo.InvariantCulture);
		}
		catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException or ArgumentException)
		{
			throw new FormatException($"`{input}` isn't a valid {FriendlyName(type)}.", e);
		}
	}

	private static string StripMention(string input) =>
		input.StartsWith('<') && input.EndsWith('>') ? input.TrimStart('<', '@', '#', '&', '!').TrimEnd('>') : input;

	private static string FriendlyName(Type type) => type switch
	{
		_ when type == typeof(ulong) => "ID",
		_ when type == typeof(bool) => "true/false value",
		_ when type.IsEnum => $"{type.Name} ({string.Join(", ", Enum.GetNames(type))})",
		_ => type.Name,
	};
}
