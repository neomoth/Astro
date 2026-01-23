using Astro.Abstractions;

namespace Astro.Bot.Configuration;

public sealed class ConfigOptionRegistry
{
	private readonly Dictionary<string, IConfigOption> options = [];
	public IEnumerable<IConfigOption> All => options.Values;
	
	public void Register(IConfigOption option) => options[option.Name] = option;
	public bool TryGet(string path, out IConfigOption option) => options.TryGetValue(path, out option!);

	public bool TrySet(string path, object? value) =>
		options.TryGetValue(path, out IConfigOption? option) && option.TrySet(value);

	public T? Get<T>(string path)
	{
		if (!options.TryGetValue(path, out IConfigOption? option)) return default;
		if (option is not IConfigOption<T> typedOption) return default;
		return (T)typedOption.Value!;
	}
	
	public void Set<T>(string path, T value)
	{
		if (!options.TryGetValue(path, out IConfigOption? option)) return;
		if (option is not IConfigOption<T> typedOption) return;
		typedOption.Set(value);
	}
}