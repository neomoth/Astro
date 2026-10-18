namespace Astro.Bot;

/// <summary>
/// Locations under the Resources directory next to the solution file.
/// </summary>
public static class ResourcePaths
{
	public static string Root { get; } = FindRoot();
	public static string Config => Path.Combine(Root, "Config");
	public static string Logs => Path.Combine(Root, "Logs");
	public static string Keys => Path.Combine(Root, "Keys");

	private static string FindRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !Directory.EnumerateFiles(dir.FullName, "*.sln").Any())
			dir = dir.Parent;
		return dir is null
			? throw new DirectoryNotFoundException("Could not find the solution (.sln) file in any parent directory.")
			: Path.Combine(dir.FullName, "Resources");
	}
}
