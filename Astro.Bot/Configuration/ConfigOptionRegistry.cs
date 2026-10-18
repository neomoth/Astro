using Astro.Abstractions;

namespace Astro.Bot.Configuration;

public sealed class ConfigOptionRegistry
{
	private readonly Dictionary<string, IConfigOption> _options = [];
	public IEnumerable<IConfigOption> All => _options.Values;

	public void Register(IConfigOption option) => _options[option.Name] = option;
	public bool TryGet(string path, out IConfigOption option) => _options.TryGetValue(path, out option!);

	public bool TrySet(string path, object? value) =>
		_options.TryGetValue(path, out var option) && option.TrySet(value);

	public T? Get<T>(string path)
	{
		if (!_options.TryGetValue(path, out var option)) return default;
		if (option is not IConfigOption<T> typedOption) return default;
		return (T)typedOption.Value!;
	}

	public void Set<T>(string path, T value)
	{
		if (!_options.TryGetValue(path, out var option)) return;
		if (option is not IConfigOption<T> typedOption) return;
		typedOption.Set(value);
	}
}
