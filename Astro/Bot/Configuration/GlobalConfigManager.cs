using System.Diagnostics;
using System.Text.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Astro.Bot.Configuration;

public static class GlobalConfig
{
	public static T? Get<T>(string path) => AstroBot.Config.Registry.Get<T>(path);
	public static void Set<T>(string path, T value) => AstroBot.Config.Registry.Set(path, value);
}

public sealed class GlobalConfigManager
{
	private readonly string resourceDir;
	private readonly string configDir;

	public ConfigOptionRegistry Registry { get; private set; }
	private const string ConfigFileName = "config.json";
	public string FullPath => Path.Join(configDir, ConfigFileName);

	private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

	public GlobalConfigManager()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && !Directory.EnumerateFiles(dir.FullName, "*.sln").Any())
			dir = dir.Parent;
		if (dir == null)
			throw new DirectoryNotFoundException("Could not find the solution (.sln) file in any parent directory.");

		resourceDir = Path.Combine(dir.FullName, "Resources");
		configDir = Path.Combine(resourceDir, "Config");

		Directory.CreateDirectory(resourceDir);
		Directory.CreateDirectory(configDir);

		Registry = new ConfigOptionRegistry();
	}
	
	public GlobalConfigManager LoadConfig()
	{
		Debug.Assert(configDir != null, nameof(configDir) + " != null");
		InitializeDefaultOptions();
		if (!File.Exists(Path.Combine(configDir, ConfigFileName)))
		{
			Save();
			return this;
		}

		var text = File.ReadAllText(Path.Combine(configDir, ConfigFileName));
		if (string.IsNullOrWhiteSpace(text)) text = "{}";
		using var doc = JsonDocument.Parse(text);
		LoadElement(doc.RootElement);
		Save();
		return this;
	}

	public void Save()
	{
		var root = new Dictionary<string, object?>();
		foreach (var option in Registry.All)
			InsertPath(root, option.Name, option.Value);
		var json = JsonSerializer.Serialize(root, SerializerOptions);
		File.WriteAllText(Path.Combine(configDir!, ConfigFileName), json);
	}

	private static void InsertPath(Dictionary<string, object?> root, string path, object? value)
	{
		var parts = path.Split('.');
		var current = root;

		for (int i = 0; i < parts.Length - 1; i++)
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
				Registry.TrySet(path, element);
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
		Registry.Register(new ConfigOption<int>("Bot.ClientId"));
		Registry.Register(new ConfigOption<string>("Bot.PublicKey"));
		Registry.Register(new ConfigOption<string[]>("Bot.Status.Statuses", []));
		Registry.Register(new ConfigOption<double>("Bot.Status.StatusUpdateInterval", 5));
		Registry.Register(new ConfigOption<string>("Bot.Status.StatusPrefix", "/help"));
		Registry.Register(new ConfigOption<int>("Bot.Help.CommandsPerPage", 6));
		Registry.Register(new ConfigOption<ulong[]>("Bot.GlobalWhitelist.Whitelist", [1134602054288015380]));
		Registry.Register(new ConfigOption<bool>("Bot.GlobalWhitelist.PreventFirstEntryRemoval", true));
		Registry.Register(new ConfigOption<string[]>("Bot.Config.ProhibitedOptions", [
			"Bot.ClientToken",
			"Bot.ClientSecret",
			"Bot.ClientId",
			"Bot.PublicKey",
			"Bot.Config.ProhibitedOptions"
		]));
	}
}