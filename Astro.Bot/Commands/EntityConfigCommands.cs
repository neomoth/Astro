using System.Collections;
using System.Reflection;
using Astro.Bot.Configuration;
using Astro.Bot.Permissions;
using Astro.DB;
using Astro.DB.Entities;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

/// <summary>
/// Shared plumbing for <c>/config</c> subcommands that edit a database config row (<typeparamref name="TEntity"/>),
/// picked by <typeparamref name="TScope"/>. Concrete commands only need a <c>[SubCommand]</c> attribute.
/// </summary>
public abstract class EntityConfigCommand<TEntity, TScope> : Command
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	// Discord allows at most 25 choices per option; past that, keys are typed freely.
	private const int MaxChoices = 25;

	[Dependency] protected readonly UserPermissionManager Permissions = null!;

	protected sealed override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var reply = TScope.GetId(command) is { } id
			? await Run(command, args, id)
			: $"There's no {TScope.Possessive} config here.";
		await command.FollowupAsync(reply, ephemeral: true);
	}

	protected abstract Task<string> Run(SocketSlashCommand command, CommandArgs args, ulong id);

	protected static SlashCommandOptionBuilder KeyOption(IEnumerable<PropertyInfo> choices)
	{
		var builder = new SlashCommandOptionBuilder().WithName("key")
			.WithDescription("The config option")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true);

		var names = choices.Select(p => p.Name).ToArray();
		if (names.Length is <= 0 or > MaxChoices) return builder;
		foreach (var name in names)
			builder.AddChoice(name, name);
		return builder;
	}

	protected static SlashCommandOptionBuilder ValueOption(string description) =>
		new SlashCommandOptionBuilder().WithName("value")
			.WithDescription(description)
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true);

	/// <summary>Finds the option named by the <c>key</c> argument and checks the user may edit it.</summary>
	protected bool TryGetEditable(CommandArgs args, ulong userId, out PropertyInfo option, out string error)
	{
		var key = args.Get<string>("key") ?? string.Empty;
		option = EntityConfig<TEntity>.Find(key)!;
		return EntityConfig<TEntity>.CanEdit(option, userId, Permissions, out error);
	}
}

public abstract class EntityConfigListCommand<TEntity, TScope> : EntityConfigCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override string Name => "list";
	public override string Description => $"Show {TScope.Possessive} config.";

	protected override async Task<string> Run(SocketSlashCommand command, CommandArgs args, ulong id)
	{
		var entity = await AstroDb.Get<TEntity>(id);
		var lines = EntityConfig<TEntity>.Options
			.Select(p => EntityConfig<TEntity>.Describe(p, p.GetValue(entity), command.User.Id, Permissions));
		return $"Showing {TScope.Possessive} config:\n```js\n{string.Join('\n', lines)}\n```";
	}
}

public abstract class EntityConfigSetCommand<TEntity, TScope> : EntityConfigCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override string Name => "set";
	public override string Description => $"Set an option in {TScope.Possessive} config.";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		KeyOption(EntityConfig<TEntity>.EditableOptions),
		ValueOption("The new value. Use \"null\" to clear it, or commas between items for lists.")
	];

	protected override async Task<string> Run(SocketSlashCommand command, CommandArgs args, ulong id)
	{
		if (!TryGetEditable(args, command.User.Id, out var option, out var error)) return error;

		object? value;
		try
		{
			value = EntityConfig<TEntity>.Parse(args.Get<string>("value") ?? string.Empty, option.PropertyType);
		}
		catch (FormatException e)
		{
			return e.Message;
		}

		if (value is null && option.PropertyType.IsValueType && Nullable.GetUnderlyingType(option.PropertyType) is null)
			return $"`{option.Name}` can't be null.";

		await AstroDb.Update<TEntity>(id, e => option.SetValue(e, value));
		return $"Set `{option.Name}` to `{EntityConfig<TEntity>.Format(value)}`.";
	}
}

public abstract class EntityConfigResetCommand<TEntity, TScope> : EntityConfigCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override string Name => "reset";
	public override string Description => $"Reset an option in {TScope.Possessive} config to its default.";

	public override List<SlashCommandOptionBuilder> Options => [KeyOption(EntityConfig<TEntity>.EditableOptions)];

	protected override async Task<string> Run(SocketSlashCommand command, CommandArgs args, ulong id)
	{
		if (!TryGetEditable(args, command.User.Id, out var option, out var error)) return error;

		var value = EntityConfig<TEntity>.DefaultValue(option);
		await AstroDb.Update<TEntity>(id, e => option.SetValue(e, value));
		return $"Reset `{option.Name}` to `{EntityConfig<TEntity>.Format(value)}`.";
	}
}

/// <summary>Base for <c>add</c>/<c>remove</c>, which change one item of a list option.</summary>
public abstract class EntityConfigListEditCommand<TEntity, TScope> : EntityConfigCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override List<SlashCommandOptionBuilder> Options =>
		[KeyOption(EntityConfig<TEntity>.ListOptions), ValueOption("The item")];

	/// <summary>Changes <paramref name="list"/>, returning the reply, or null if nothing changed.</summary>
	protected abstract string? Edit(IList list, object? item, string itemText, string key);

	protected override async Task<string> Run(SocketSlashCommand command, CommandArgs args, ulong id)
	{
		if (!TryGetEditable(args, command.User.Id, out var option, out var error)) return error;
		if (EntityConfig<TEntity>.ListElementType(option) is not { } elementType)
			return $"`{option.Name}` isn't a list.";

		object? item;
		try
		{
			item = EntityConfig<TEntity>.Parse(args.Get<string>("value") ?? string.Empty, elementType);
		}
		catch (FormatException e)
		{
			return e.Message;
		}

		string? reply = null;
		await AstroDb.Update<TEntity>(id, e =>
		{
			// A fresh list, so EF sees the property change.
			var list = (IList)(option.GetValue(e) is { } current
				? Activator.CreateInstance(option.PropertyType, current)!
				: Activator.CreateInstance(option.PropertyType)!);
			reply = Edit(list, item, EntityConfig<TEntity>.Format(item), option.Name);
			option.SetValue(e, list);
		});
		return reply ?? "Nothing changed.";
	}
}

public abstract class EntityConfigAddCommand<TEntity, TScope> : EntityConfigListEditCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override string Name => "add";
	public override string Description => $"Add an item to a list in {TScope.Possessive} config.";

	protected override string Edit(IList list, object? item, string itemText, string key)
	{
		if (list.Contains(item)) return $"`{key}` already contains `{itemText}`.";
		list.Add(item);
		return $"Added `{itemText}` to `{key}`.";
	}
}

public abstract class EntityConfigRemoveCommand<TEntity, TScope> : EntityConfigListEditCommand<TEntity, TScope>
	where TEntity : class, IConfigEntity, new()
	where TScope : IConfigScope<TEntity>
{
	public override string Name => "remove";
	public override string Description => $"Remove an item from a list in {TScope.Possessive} config.";

	protected override string Edit(IList list, object? item, string itemText, string key)
	{
		if (!list.Contains(item)) return $"`{key}` doesn't contain `{itemText}`.";
		list.Remove(item);
		return $"Removed `{itemText}` from `{key}`.";
	}
}
