using System.Reflection;
using Astro.Bot.Configuration;
using Discord.WebSocket;
using Astro.Abstractions;
using IoC;
using RTLogging;

namespace Astro.Bot.Behaviors;

public sealed class BehaviorManager : Manager
{
	[Dependency] private readonly IDependencyCollection _dependencies = null!;
	[Dependency] private readonly GlobalConfigManager _config = null!;

	private readonly Dictionary<Type, IBehavior> _behaviorDict = [];
	public IReadOnlyCollection<IBehavior> Behaviors => _behaviorDict.Values;

	protected override string SawmillName => "astro.behaviors";

	/// <summary>
	/// Discovers every [Behavior] class and registers its config options. Call before the config file is read so
	/// saved values for those options are applied.
	/// </summary>
	public void Load()
	{
		Unload();

		var types = Assembly.GetExecutingAssembly().GetTypes()
			.Where(t =>
				typeof(IBehavior).IsAssignableFrom(t) &&
				t.IsClass &&
				t is { IsAbstract: false, ContainsGenericParameters: false } &&
				t.IsDefined(typeof(BehaviorAttribute)));

		foreach (var type in types)
		{
			try
			{
				var instance = (IBehavior)Activator.CreateInstance(type)!;
				_dependencies.InjectDependencies(instance);
				if (_behaviorDict.Values.Any(b => b.Name == instance.Name))
				{
					Log.Warn("Behavior {type} has duplicate name {name} and will not be loaded.", type.Name, instance.Name);
					instance.Dispose();
					continue;
				}

				foreach (var option in instance.CreateConfigOptions())
					_config.Registry.Register(option);
				_behaviorDict[type] = instance;
			}
			catch (Exception e)
			{
				Log.Log(LogLevel.Error, e, "Failed to load behavior {type}.", type.Name);
			}
		}

		Log.Verbose("Loaded {count} behaviors.", _behaviorDict.Count);
	}

	public void AttachAll(DiscordSocketClient client)
	{
		foreach (var behavior in Behaviors)
		{
			try
			{
				behavior.Attach(client);
			}
			catch (Exception e)
			{
				Log.Log(LogLevel.Error, e, "Failed to attach behavior {name}.", behavior.Name);
			}
		}
	}

	public void DetachAll()
	{
		foreach (var behavior in Behaviors)
			behavior.Detach();
	}

	public T? Get<T>() where T : class, IBehavior =>
		_behaviorDict.GetValueOrDefault(typeof(T)) as T;

	private void Unload()
	{
		foreach (var behavior in Behaviors)
			behavior.Dispose();
		_behaviorDict.Clear();
	}
}
