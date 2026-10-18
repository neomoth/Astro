using Astro.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace Astro.DB;

/// <summary>
/// Entry point for everything database related.
/// <code language="csharp">
/// var guild = await AstroDb.Guild(guildId);
/// await AstroDb.UpdateGuild(guildId, g => g.LoggingChannelId = channelId);
/// var admins = await AstroDb.Query(db => db.Users.Where(u => u.Permissions.HasFlag(UserPermissions.ManageBotConfig)).ToListAsync());
/// </code>
/// </summary>
public static class AstroDb
{
	private const string DatabaseFileName = "astro.db";
	private static DbContextOptions<AstroDbContext>? _options;

	public static string FullPath { get; private set; } = Path.Combine(FindResourceDir(), "Data", DatabaseFileName);

	/// <summary>
	/// Creates the database if missing and applies any pending migrations. Call once at startup.
	/// </summary>
	public static async Task Initialize()
	{
		await using var db = Create();
		if (db.Database.HasPendingModelChanges())
			throw new InvalidOperationException(
				"The entities in Astro.DB/Entities were changed but no migration was made for it. Run Astro.DB/Tools/migrate.sh, then start the bot again.");
		await db.Database.MigrateAsync();
	}

	/// <summary>
	/// Creates a fresh context. Contexts are cheap and should be short-lived, so dispose it when done
	/// (<c>await using var db = AstroDb.Create();</c>).
	/// </summary>
	public static AstroDbContext Create()
	{
		if (_options is not null) return new AstroDbContext(_options);
		Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
		_options = new DbContextOptionsBuilder<AstroDbContext>()
			.UseSqlite($"Data Source={FullPath}")
			.Options;

		return new AstroDbContext(_options);
	}

	#region Generic helpers

	/// <summary>
	/// Gets the row with the given ID, or a new one filled with default values if it doesn't exist yet.
	/// Nothing is saved; use <see cref="Update{T}"/> to make changes.
	/// </summary>
	public static async Task<T> Get<T>(ulong id) where T : class, IConfigEntity, new()
	{
		await using var db = Create();
		return await db.Set<T>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == id) ?? new T { Id = id };
	}

	/// <summary>
	/// Gets (or creates) the row with the given ID, applies <paramref name="change"/> and saves it.
	/// </summary>
	public static async Task<T> Update<T>(ulong id, Action<T> change) where T : class, IConfigEntity, new()
	{
		await using var db = Create();
		var entity = await db.Set<T>().FindAsync(id);
		if (entity is null)
		{
			entity = new T { Id = id };
			db.Add(entity);
		}

		change(entity);
		await db.SaveChangesAsync();
		return entity;
	}

	/// <summary>
	/// Deletes the row with the given ID, resetting it to defaults. Returns false if it didn't exist.
	/// </summary>
	public static async Task<bool> Delete<T>(ulong id) where T : class, IConfigEntity
	{
		await using var db = Create();
		// ExecuteDelete skips the change tracker, so this one raises its own event.
		if (await db.Set<T>().Where(e => e.Id == id).ExecuteDeleteAsync() == 0) return false;
		await ConfigEvents.Raise([new ConfigChange(typeof(T), id, null, ConfigChangeKind.Deleted, [])]);
		return true;
	}

	/// <summary>
	/// Runs an arbitrary read query, e.g. <c>AstroDb.Query(db => db.Guilds.CountAsync())</c>.
	/// </summary>
	public static async Task<TResult> Query<TResult>(Func<AstroDbContext, Task<TResult>> query)
	{
		await using var db = Create();
		return await query(db);
	}

	/// <summary>
	/// Runs arbitrary work against the database and saves any changes afterwards.
	/// </summary>
	public static async Task Run(Func<AstroDbContext, Task> work)
	{
		await using var db = Create();
		await work(db);
		await db.SaveChangesAsync();
	}

	#endregion

	#region Shortcuts

	public static Task<GuildConfig> Guild(ulong guildId) => Get<GuildConfig>(guildId);
	public static Task<GuildConfig> UpdateGuild(ulong guildId, Action<GuildConfig> change) => Update(guildId, change);

	public static Task<UserConfig> User(ulong userId) => Get<UserConfig>(userId);
	public static Task<UserConfig> UpdateUser(ulong userId, Action<UserConfig> change) => Update(userId, change);

	#endregion

	private static string FindResourceDir()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir is not null && !Directory.EnumerateFiles(dir.FullName, "*.sln").Any())
			dir = dir.Parent;
		return dir is null
			? throw new DirectoryNotFoundException("Could not find the solution (.sln) file in any parent directory.")
			: Path.Combine(dir.FullName, "Resources");
	}
}
