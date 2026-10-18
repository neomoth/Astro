using System.Text.Json;
using Astro.Abstractions;
using Astro.Bot.Configuration;
using Astro.DB.Entities;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

[RootCommand]
[RequirePermissions(GuildPermission.ModerateMembers, Global = UserPermissions.Config,
	GlobalMode = GlobalPermissionMode.Bypass)]
public sealed class ConfigCommand : Command
{
	public override string Name => "config";
	public override string Description => "Bot config command subgroup";
	protected override Task ExecuteCommand(SocketSlashCommand command, CommandArgs args) => Task.CompletedTask;

	public static object? Parse(string input)
	{
		if (!LooksLikeJson(input))
			input = $"\"{input}\"";

		using var doc = JsonDocument.Parse(input);
		return ParseElement(doc.RootElement);
	}

	private static object? ParseElement(JsonElement element) =>
		element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => ParseNumber(element),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null => null,
			JsonValueKind.Array => element.EnumerateArray()
				.Select(ParseElement)
				.ToArray(),
			JsonValueKind.Object => element.EnumerateObject()
				.ToDictionary(p => p.Name, p => ParseElement(p.Value)),
			_ => null
		};

	private static object ParseNumber(JsonElement element)
	{
		if (element.TryGetInt64(out var l))
			return l;

		if (element.TryGetDouble(out var d))
			return d;

		return element.GetDecimal();
	}

	private static bool LooksLikeJson(string input)
	{
		input = input.Trim();
		return input.StartsWith('{')
		       || input.StartsWith('[')
		       || input.StartsWith('"')
		       || input is "true" or "false" or "null"
		       || char.IsDigit(input[0]) || input[0] == '-';
	}
}

#region Command Groups
[CommandGroup(typeof(ConfigCommand))]
[RequirePermissions(Global=UserPermissions.Danger)]
public sealed class ConfigGlobalSubCommandGroup : CommandGroup
{
	public override string Name => "global";
	public override string Description => "Edit the global config";
	public override bool HideIfCantExecute => true;
}
#endregion

#region Global Subcommands
[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalReloadSubCommand : Command
{
	[Dependency] private readonly GlobalConfigManager _config = null!;

	public override string Name => "reload";
	public override string Description => "Reloads the config file, in case it was updated manually.";
	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		_config.LoadConfig();
		await command.FollowupAsync("Reloaded configuration file.");
	}
}

[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalResetSubCommand : Command
{
	[Dependency] private readonly GlobalConfigManager _config = null!;

	public override string Name => "reset";
	public override string Description => "Reset a configuration option to its default value";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder()
			.WithName("key")
			.WithDescription("The name of the config option you want to reset")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		var key = string.Empty;

		await command.DeferAsync(ephemeral: true);

		var group = command.Data.Options.First();
		var sub = group.Options.First();

		foreach (var option in sub.Options)
		{
			if (option.Name == "key")
				key = (string)option.Value!;
		}

		var bannedOptions = _config.Registry.Get<string[]>("Bot.Config.ProhibitedOptions");
		if (bannedOptions.Contains(key))
		{
			await command.FollowupAsync($"`{key}` is a protected configuration key and cannot be modified.");
			return;
		}

		if (!_config.Registry.TryGet(key, out var cfg))
		{
			await command.FollowupAsync("Invalid configuration key.");
			return;
		}

		if (cfg.DefaultValue is null)
		{
			await command.FollowupAsync($"Configuration option `{key}` does not have a default value.");
			return;
		}

		if (!_config.Registry.TrySet(key, cfg.DefaultValue))
		{
			await command.FollowupAsync($"Failed to reset `{key}`. Expected type `{cfg.Type.Name}`.");
			return;
		}

		_config.Save();
		await command.FollowupAsync($"Reset `{key}` to its default value.");
	}
}

[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalSetSubCommand : Command
{
	[Dependency] private readonly GlobalConfigManager _config = null!;

	public override string Name => "set";
	public override string Description => "Set a configuration option to a new value";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder()
			.WithName("key")
			.WithDescription("The name of the config option you want to modify")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true),

		new SlashCommandOptionBuilder()
			.WithName("value")
			.WithDescription("The value you want to set")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		var key = string.Empty;
		var rawValue = string.Empty;

		await command.DeferAsync(ephemeral: true);

		var group = command.Data.Options.First();
		var sub = group.Options.First();

		foreach (var option in sub.Options)
		{
			switch (option.Name)
			{
				case "key":
					key = (string)option.Value!;
					break;

				case "value":
					rawValue = (string)option.Value!;
					break;
			}
		}

		var bannedOptions = _config.Registry.Get<string[]>("Bot.Config.ProhibitedOptions");
		if (bannedOptions.Contains(key))
		{
			await command.FollowupAsync($"`{key}` is a protected configuration key and cannot be modified.");
			return;
		}

		if (!_config.Registry.TryGet(key, out var cfg))
		{
			await command.FollowupAsync("Invalid configuration key.");
			return;
		}

		var parsed = ConfigCommand.Parse(rawValue);

		if (parsed is null)
		{
			await command.FollowupAsync("Cannot set configuration value to null.");
			return;
		}

		var expectedType = cfg.Type;
		if (!expectedType.IsInstanceOfType(parsed))
		{
			await command.FollowupAsync($"Invalid value type. Expected `{expectedType.Name}`.");
			return;
		}

		if (!_config.Registry.TrySet(key, parsed))
		{
			await command.FollowupAsync(
				$"Invalid value for `{key}`. Expected `{expectedType}`.");
			return;
		}

		_config.Save();
		await command.FollowupAsync($"Set `{key}` to `{rawValue}`.");
	}
}

[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalAddSubCommand : Command
{
	[Dependency] private readonly GlobalConfigManager _config = null!;

	public override string Name => "add";
	public override string Description => "Add a value to an array config option, optionally specify an index";

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("key")
			.WithDescription("The name of the config option you want to modify")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true),
		new SlashCommandOptionBuilder().WithName("value")
			.WithDescription("The value you want to add to the option")
			.WithType(ApplicationCommandOptionType.String)
			.WithRequired(true),
		new SlashCommandOptionBuilder().WithName("index")
			.WithDescription("The index you want to insert this value at")
			.WithType(ApplicationCommandOptionType.Integer)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		var key = string.Empty;
		var rawValue = string.Empty;
		int? index = null;

		await command.DeferAsync(ephemeral: true);

		var group = command.Data.Options.First();
		var sub = group.Options.First();

		foreach (var option in sub.Options)
		{
			switch (option.Name)
			{
				case "key":
					key = (string)option.Value!;
					break;

				case "value":
					rawValue = (string)option.Value!;
					break;

				case "index":
					index = Convert.ToInt32(option.Value);
					break;
			}
		}

		var bannedOptions = _config.Registry.Get<string[]>("Bot.Config.ProhibitedOptions");
		if (bannedOptions.Contains(key))
		{
			await command.FollowupAsync($"`{key}` is a protected configuration key and cannot be modified.");
			return;
		}

		if (!_config.Registry.TryGet(key, out var cfg))
		{
			await command.FollowupAsync("Invalid configuration key.");
			return;
		}

		if (cfg.Value is not Array arr)
		{
			await command.FollowupAsync($"Config option `{key}` is not an array.");
			return;
		}

		var parsed = ConfigCommand.Parse(rawValue);
		if (parsed is null)
		{
			await command.FollowupAsync("Cannot insert null into array.");
			return;
		}

		var elementType = arr.GetType().GetElementType()!;
		if (!elementType.IsInstanceOfType(parsed))
		{
			await command.FollowupAsync(
				$"Invalid value type. Expected `{elementType.Name}`.");
			return;
		}

		if (index is not null && (index < 0 || index > arr.Length))
		{
			await command.FollowupAsync("Index out of range.");
			return;
		}
		var newArray = Array.CreateInstance(elementType, arr.Length + 1);

		if (index is null)
		{
			Array.Copy(arr, newArray, arr.Length);
			newArray.SetValue(parsed, arr.Length);
		}
		else
		{
			Array.Copy(arr, 0, newArray, 0, index.Value);
			newArray.SetValue(parsed, index.Value);
			Array.Copy(arr, index.Value, newArray, index.Value + 1, arr.Length - index.Value);
		}

		if (!_config.Registry.TrySet(key, newArray))
		{
			await command.FollowupAsync($"Invalid value for `{key}`. Expected `{cfg.Type}`.");
			return;
		}

		_config.Save();
		await command.FollowupAsync(index is null
			? $"Appended `{rawValue}` to `{key}`."
			: $"Added `{rawValue}` to `{key}` at position `{index}`.");
	}
}

[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalRemoveSubCommand : Command
{
    [Dependency] private readonly GlobalConfigManager _config = null!;

    public override string Name => "remove";
    public override string Description => "Remove a value from an array config option";

    public override List<SlashCommandOptionBuilder> Options =>
    [
        new SlashCommandOptionBuilder().WithName("key")
            .WithDescription("The config key to remove from")
            .WithType(ApplicationCommandOptionType.String)
            .WithRequired(true),
        new SlashCommandOptionBuilder().WithName("value")
            .WithDescription("The value to remove")
            .WithType(ApplicationCommandOptionType.String),
        new SlashCommandOptionBuilder().WithName("index")
            .WithDescription("The index to remove")
            .WithType(ApplicationCommandOptionType.Integer)
    ];

    protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
    {
        var key = string.Empty;
        string? rawValue = null;
        int? index = null;

        await command.DeferAsync(ephemeral: true);

        var group = command.Data.Options.First();
        var sub = group.Options.First();

        foreach (var option in sub.Options)
        {
            switch (option.Name)
            {
                case "key":
                    key = (string)option.Value!;
                    break;
                case "value":
                    rawValue = (string?)option.Value;
                    break;
                case "index":
                    index = Convert.ToInt32(option.Value);
                    break;
            }
        }

        var bannedOptions = _config.Registry.Get<string[]>("Bot.Config.ProhibitedOptions");
        if (bannedOptions.Contains(key))
        {
            await command.FollowupAsync($"`{key}` is a protected configuration key and cannot be modified.");
            return;
        }

        if (!_config.Registry.TryGet(key, out var cfg))
        {
            await command.FollowupAsync("Invalid configuration key.");
            return;
        }

        if (cfg.Value is not Array arr)
        {
            await command.FollowupAsync($"Config option `{key}` is not an array.");
            return;
        }

        var elementType = arr.GetType().GetElementType()!;
        int removeIndex;

        if (index is not null)
        {
            if (index < 0 || index >= arr.Length)
            {
                await command.FollowupAsync("Index out of range.");
                return;
            }
            removeIndex = index.Value;
        }
        else if (rawValue is not null)
        {
            object parsed;
            try
            {
                parsed = Convert.ChangeType(rawValue, elementType);
            }
            catch
            {
                await command.FollowupAsync($"Invalid value type. Expected `{elementType.Name}`.");
                return;
            }

            removeIndex = Array.IndexOf(arr, parsed);
            if (removeIndex == -1)
            {
                await command.FollowupAsync($"Value `{rawValue}` not found in `{key}`.");
                return;
            }
        }
        else
        {
            await command.FollowupAsync("You must specify either a value or an index to remove.");
            return;
        }

        if (key == "Bot.Hosts.Users")
        {
            var preventFirst = _config.Registry.Get<bool>("Bot.Hosts.PreventFirstEntryRemoval");
            if (preventFirst && removeIndex == 0)
            {
                await command.FollowupAsync("Cannot remove the first host due to configuration.");
                return;
            }
        }

        var newArray = Array.CreateInstance(elementType, arr.Length - 1);
        if (removeIndex > 0)
            Array.Copy(arr, 0, newArray, 0, removeIndex);
        if (removeIndex < arr.Length - 1)
            Array.Copy(arr, removeIndex + 1, newArray, removeIndex, arr.Length - removeIndex - 1);

        if (!_config.Registry.TrySet(key, newArray))
        {
            await command.FollowupAsync($"Failed to remove value from `{key}`.");
            return;
        }

        _config.Save();
        await command.FollowupAsync(index is not null
            ? $"Removed element at index `{removeIndex}` from `{key}`."
            : $"Removed value `{rawValue}` from `{key}`.");
    }
}

[SubCommand(typeof(ConfigGlobalSubCommandGroup))]
public sealed class ConfigGlobalListSubCommand : Command
{
	[Dependency] private readonly GlobalConfigManager _config = null!;

	public override string Name => "list";
	public override string Description => "Lists all of the global bot properties";
	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);
		var bannedOptions = _config.Registry.Get<string[]>("Bot.Config.ProhibitedOptions");
		var optionList = _config.Registry.All.Where(opt => !bannedOptions.Contains(opt.Name));
		await command.FollowupAsync($"Current configuration:\n```js\n{
			string.Join('\n', optionList.Select(opt=>$"{opt.Name}: {ConfigOption.PrettyString(opt.Value)}"))
		}\n```");
	}
}
#endregion

#region Server Config
[CommandGroup(typeof(ConfigCommand))]
[AvailableIn(CommandLocation.Server)]
[RequirePermissions(GuildPermission.ManageGuild, Global = UserPermissions.GlobalAdmin,
	GlobalMode = GlobalPermissionMode.Bypass)]
public sealed class ConfigServerSubCommandGroup : CommandGroup
{
	public override string Name => "server";
	public override string Description => "Edit this server's config";
}

[SubCommand(typeof(ConfigServerSubCommandGroup))]
public sealed class ConfigServerListSubCommand : EntityConfigListCommand<GuildConfig, ServerConfigScope>;

[SubCommand(typeof(ConfigServerSubCommandGroup))]
public sealed class ConfigServerSetSubCommand : EntityConfigSetCommand<GuildConfig, ServerConfigScope>;

[SubCommand(typeof(ConfigServerSubCommandGroup))]
public sealed class ConfigServerResetSubCommand : EntityConfigResetCommand<GuildConfig, ServerConfigScope>;

[SubCommand(typeof(ConfigServerSubCommandGroup))]
public sealed class ConfigServerAddSubCommand : EntityConfigAddCommand<GuildConfig, ServerConfigScope>;

[SubCommand(typeof(ConfigServerSubCommandGroup))]
public sealed class ConfigServerRemoveSubCommand : EntityConfigRemoveCommand<GuildConfig, ServerConfigScope>;
#endregion

#region User Config
/// <summary>Your own user config. Open to everyone, everywhere; restricted options stay locked.</summary>
[CommandGroup(typeof(ConfigCommand))]
[RequirePermissions]
public sealed class ConfigUserSubCommandGroup : CommandGroup
{
	public override string Name => "user";
	public override string Description => "Edit your own config";
}

[SubCommand(typeof(ConfigUserSubCommandGroup))]
public sealed class ConfigUserListSubCommand : EntityConfigListCommand<UserConfig, UserConfigScope>;

[SubCommand(typeof(ConfigUserSubCommandGroup))]
public sealed class ConfigUserSetSubCommand : EntityConfigSetCommand<UserConfig, UserConfigScope>;

[SubCommand(typeof(ConfigUserSubCommandGroup))]
public sealed class ConfigUserResetSubCommand : EntityConfigResetCommand<UserConfig, UserConfigScope>;
#endregion
