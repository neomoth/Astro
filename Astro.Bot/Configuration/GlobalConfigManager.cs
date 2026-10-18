using System.Diagnostics;
using System.Text.Json;
using Astro.Abstractions;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Astro.Bot.Configuration;

/// <summary>
/// Shorthand for reading and writing config values without injecting <see cref="GlobalConfigManager"/>,
/// mainly for static classes.
/// </summary>
public static class GlobalConfig
{
	private static GlobalConfigManager _manager => field ??= Dependencies.Resolve<GlobalConfigManager>();

	public static T? Get<T>(string path) => _manager.Registry.Get<T>(path);
	public static void Set<T>(string path, T value) => _manager.Registry.Set(path, value);
}

public sealed class GlobalConfigManager : Manager
{
	protected override string SawmillName => "astro.config";

	public Action<ConfigOptionRegistry>? OnConfigLoaded;

	private readonly string _configDir;

	public ConfigOptionRegistry Registry { get; }
	private const string ConfigFileName = "config.json";
	public string FullPath => Path.Join(_configDir, ConfigFileName);

	private static readonly JsonSerializerOptions SerializerOptions = new()
	{
		WriteIndented = true,
		IndentCharacter = '\t',
		IndentSize = 1
	};

	/// Old option paths mapped to their current ones, so values in existing config files carry over.
	private static readonly Dictionary<string, string> RenamedOptions = new()
	{
		["Bot.GlobalWhitelist.Whitelist"] = "Bot.Hosts.Users",
		["Bot.GlobalWhitelist.PreventFirstEntryRemoval"] = "Bot.Hosts.PreventFirstEntryRemoval"
	};

	public GlobalConfigManager()
	{
		var resourceDir = ResourcePaths.Root;
		_configDir = ResourcePaths.Config;

		Directory.CreateDirectory(resourceDir);
		Directory.CreateDirectory(_configDir);

		Registry = new ConfigOptionRegistry();
	}

	public void LoadConfig()
	{
		Debug.Assert(_configDir is not null, nameof(_configDir) + " != null");
		InitializeDefaultOptions();
		if (!File.Exists(Path.Combine(_configDir, ConfigFileName)))
		{
			Save();
			return;
		}

		var text = File.ReadAllText(Path.Combine(_configDir, ConfigFileName));
		if (string.IsNullOrWhiteSpace(text)) text = "{}";
		using var doc = JsonDocument.Parse(text);
		LoadElement(doc.RootElement);
		Save();

		OnConfigLoaded?.Invoke(Registry);
	}

	/// Raised after the config file is written, including after every load.
	public event Action? Saved;

	public void Save()
	{
		var root = new Dictionary<string, object?>();
		foreach (var option in Registry.All)
			InsertPath(root, option.Name, option.Value);
		var json = JsonSerializer.Serialize(root, SerializerOptions);
		File.WriteAllText(Path.Combine(_configDir, ConfigFileName), json);
		Saved?.Invoke();
	}

	private static void InsertPath(Dictionary<string, object?> root, string path, object? value)
	{
		var parts = path.Split('.');
		var current = root;

		for (var i = 0; i < parts.Length - 1; i++)
		{
			if (!current.TryGetValue(parts[i], out var next) ||
			    next is not Dictionary<string, object?> dict)
			{
				dict = [];
				current[parts[i]] = dict;
			}

			current = dict;
		}

		current[parts[^1]] = value;
	}

	private void LoadElement(JsonElement element, string path = "")
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach (var prop in element.EnumerateObject())
				{
					var next = string.IsNullOrEmpty(path) ? prop.Name : $"{path}.{prop.Name}";
					LoadElement(prop.Value, next);
				}

				break;
			case JsonValueKind.Array:
			case JsonValueKind.String:
			case JsonValueKind.Number:
			case JsonValueKind.True:
			case JsonValueKind.False:
				Registry.TrySet(RenamedOptions.GetValueOrDefault(path, path), element);
				break;
			case JsonValueKind.Null:
			case JsonValueKind.Undefined:
			default:
				break;
		}
	}

	private void InitializeDefaultOptions()
	{
		Registry.Register(new ConfigOption<string>("Bot.ClientToken"));
		Registry.Register(new ConfigOption<string>("Bot.ClientSecret"));
		Registry.Register(new ConfigOption<ulong>("Bot.ClientId"));
		Registry.Register(new ConfigOption<string>("Bot.PublicKey"));
		Registry.Register(new ConfigOption<bool>("Bot.OAuth.Enabled", false));
		Registry.Register(new ConfigOption<string>("Bot.OAuth.ListenUrl", "http://localhost:5000/"));
		Registry.Register(new ConfigOption<string>("Bot.OAuth.PublicUrl", "http://localhost:5000"));
		Registry.Register(new ConfigOption<string[]>("Bot.OAuth.Scopes", ["identify"]));
		Registry.Register(new ConfigOption<string[]>("Bot.Status.Statuses", []));
		Registry.Register(new ConfigOption<double>("Bot.Status.StatusUpdateInterval", 5));
		Registry.Register(new ConfigOption<string>("Bot.Status.StatusPrefix", "/help"));
		Registry.Register(new ConfigOption<int>("Bot.Help.CommandsPerPage", 6));
		Registry.Register(new ConfigOption<ulong[]>("Bot.Hosts.Users", []));
		Registry.Register(new ConfigOption<bool>("Bot.Hosts.PreventFirstEntryRemoval", true));
		Registry.Register(new ConfigOption<string[]>("Bot.Config.ProhibitedOptions", [
			"Bot.ClientToken",
			"Bot.ClientSecret",
			"Bot.ClientId",
			"Bot.PublicKey",
			"Bot.Config.ProhibitedOptions"
		]));
	}
}
