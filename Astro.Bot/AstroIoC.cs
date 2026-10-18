using Astro.Bot.Behaviors;
using Astro.Bot.Commands;
using Astro.Bot.Configuration;
using Astro.Bot.Managers;
using Astro.Bot.OAuth;
using Astro.Bot.Security;
using Astro.Bot.Permissions;
using IoC;

namespace Astro.Bot;

/// <summary>
/// Every service the bot provides through IoC. Add new managers here; they're constructed and have their
/// <c>[Dependency]</c> fields injected when the graph is built on startup, and anything implementing
/// <see cref="IPostInjectInit"/> gets <see cref="IPostInjectInit.PostInject"/> called once everything is injected.
/// </summary>
/// <remarks>
/// <see cref="RTLogging.ILogManager"/> isn't registered here: it's created and configured in <see cref="Program"/>
/// first, so services can log from their constructors and <c>PostInject</c>.
/// </remarks>
internal static class AstroIoC
{
	private static readonly List<Type> RegisteredTypes = [];

	/// <summary>Every type registered by <see cref="Register"/>, in registration order.</summary>
	public static IReadOnlyList<Type> Registered => RegisteredTypes;

	public static void Register(IDependencyCollection deps)
	{
		RegisteredTypes.Clear();
		void Add<T>() where T : class
		{
			deps.Register<T>();
			RegisteredTypes.Add(typeof(T));
		}

		Add<AstroBot>();
		Add<GlobalConfigManager>();
		Add<UserPermissionManager>();
		Add<HostManager>();
		Add<SecretProtector>();
		Add<OAuthManager>();
		Add<CommandManager>();
		Add<BehaviorManager>();
		Add<NicknameManager>();
	}
}
