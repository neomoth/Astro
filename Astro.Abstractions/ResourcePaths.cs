namespace Astro.Abstractions;

/// <summary>
/// Where the bot keeps its files.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Development</b> (any build run from the repo): the <c>Resources</c> folder next to the solution, as
/// <c>Config</c>, <c>Data</c>, <c>Logs</c> and <c>Keys</c>.</item>
/// <item><b>Release</b> (built by Astro.Packaging, i.e. with <c>AstroRelease=true</c>): the platform's usual folders.
/// Linux: <c>~/.config/astro</c>, <c>~/.local/share/astro</c> and <c>~/.local/state/astro/logs</c> (honouring the
/// <c>XDG_*</c> variables). Windows: <c>%APPDATA%\Astro</c> and <c>%LOCALAPPDATA%\Astro</c>. macOS:
/// <c>~/Library/Application Support/Astro</c> and <c>~/Library/Logs/Astro</c>.</item>
/// <item><b>Override</b> (either kind): the <c>ASTRO_HOME</c> environment variable or <c>--home &lt;dir&gt;</c> puts
/// everything in one folder, for portable installs or containers.</item>
/// </list>
/// The secret key lives under the config folder, away from the database, so a copy of the data folder alone can't
/// decrypt anything.
/// </remarks>
public static class ResourcePaths
{
	private const string HomeEnvironmentVariable = "ASTRO_HOME";

	private static Layout? _layout;
	private static Layout _current => _layout ??= Resolve(Environment.GetEnvironmentVariable(HomeEnvironmentVariable));

	/// <summary>How the paths were chosen, for logging.</summary>
	public static string Mode => _current.Mode;
	public static string Config => _current.Config;
	public static string Data => _current.Data;
	public static string Logs => _current.Logs;
	public static string Keys => _current.Keys;

	/// <summary>
	/// Puts everything under <paramref name="home"/> (e.g. from <c>--home</c>). Must be called before any path is used.
	/// </summary>
	public static void UseHome(string home)
	{
		if (_layout is not null)
			throw new InvalidOperationException("ResourcePaths.UseHome must be called before any path is read.");
		_layout = Resolve(home);
	}

	private static Layout Resolve(string? home)
	{
		// ReSharper disable once InvertIf
		if (!string.IsNullOrWhiteSpace(home))
		{
			var root = Path.GetFullPath(home);
			return new Layout($"{HomeEnvironmentVariable}/--home", Path.Combine(root, "config"), Path.Combine(root, "data"),
				Path.Combine(root, "logs"), Path.Combine(root, "config", "keys"));
		}

#if ASTRO_RELEASE
		return Platform();
#else
		return Development();
#endif
	}

	private static Layout Development()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !Directory.EnumerateFiles(dir.FullName, "*.sln").Any())
			dir = dir.Parent;
		if (dir is null)
			throw new DirectoryNotFoundException(
				"Could not find the solution (.sln) file in any parent directory. Development builds keep their files in " +
				$"the repo's Resources folder; set {HomeEnvironmentVariable} to run elsewhere, or build a release with Astro.Packaging.");

		var resources = Path.Combine(dir.FullName, "Resources");
		return new Layout("development", Path.Combine(resources, "Config"), Path.Combine(resources, "Data"),
			Path.Combine(resources, "Logs"), Path.Combine(resources, "Keys"));
	}

	private static Layout Platform()
	{
		var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

		if (OperatingSystem.IsWindows())
		{
			var roaming = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Astro");
			var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Astro");
			return new Layout("release", roaming, local, Path.Combine(local, "Logs"), Path.Combine(roaming, "Keys"));
		}

		if (OperatingSystem.IsMacOS())
		{
			var support = Path.Combine(userHome, "Library", "Application Support", "Astro");
			return new Layout("release", Path.Combine(support, "Config"), Path.Combine(support, "Data"),
				Path.Combine(userHome, "Library", "Logs", "Astro"), Path.Combine(support, "Config", "Keys"));
		}

		var config = Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "astro");
		return new Layout("release", config, Path.Combine(Xdg("XDG_DATA_HOME", ".local", "share"), "astro"),
			Path.Combine(Xdg("XDG_STATE_HOME", ".local", "state"), "astro", "logs"), Path.Combine(config, "keys"));

		// Linux and other Unix-likes: XDG base directories.
		string Xdg(string variable, params string[] fallback) =>
			Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value)
				? value
				: Path.Combine([userHome, ..fallback]);
	}

	// ReSharper disable MemberHidesStaticFromOuterClass
	private sealed record Layout(string Mode, string Config, string Data, string Logs, string Keys);
}
