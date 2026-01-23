namespace Astro.Abstractions;

public interface IConfigOption
{
	/// <summary>
	/// The name of the config option.
	/// </summary>
	public string Name { get; }
	/// <summary>
	/// The value of the config option.
	/// </summary>
	public object? Value { get; }
	/// <summary>
	/// The default value, used when resetting this option.
	/// </summary>
	public object? DefaultValue { get; }
	/// <summary>
	/// The type of the config value.
	/// </summary>
	Type Type { get; }
	/// <summary>
	/// Attempt to set the config option to the passed value, returning true if it is able to, and false if the object doesn't match allowed types or otherwise fails.
	/// </summary>
	/// <param name="value"></param>
	/// <returns></returns>
	public bool TrySet(object? value);
}

public interface IConfigOption<in T> : IConfigOption
{
	/// <summary>
	/// Sets the config option to the specified value.
	/// </summary>
	/// <param name="value">The value to set the option to.</param>
	/// <typeparam name="T">The type of value it is.</typeparam>
	public void Set(T value);
}