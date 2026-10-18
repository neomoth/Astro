using Astro.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Astro.DB;

public sealed class AstroDbContext(DbContextOptions<AstroDbContext> options) : DbContext(options)
{
	public DbSet<GuildConfig> Guilds => Set<GuildConfig>();
	public DbSet<UserConfig> Users => Set<UserConfig>();
	public DbSet<OAuthToken> OAuthTokens => Set<OAuthToken>();

	public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
	{
		var changes = CollectConfigChanges();
		var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
		await ConfigEvents.Raise(changes);
		return result;
	}

	public override int SaveChanges(bool acceptAllChangesOnSuccess)
	{
		var changes = CollectConfigChanges();
		var result = base.SaveChanges(acceptAllChangesOnSuccess);
		ConfigEvents.Raise(changes).GetAwaiter().GetResult();
		return result;
	}

	/// <summary>What's about to be saved, captured before saving resets the tracked states.</summary>
	private List<ConfigChange> CollectConfigChanges()
	{
		ChangeTracker.DetectChanges();
		return ChangeTracker.Entries()
			.Where(e => e.Entity is IConfigEntity && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
			.Select(e =>
			{
				var entity = (IConfigEntity)e.Entity;
				var kind = e.State switch
				{
					EntityState.Added => ConfigChangeKind.Created,
					EntityState.Deleted => ConfigChangeKind.Deleted,
					_ => ConfigChangeKind.Updated,
				};
				var changed = e.Properties
					.Where(p => !p.Metadata.IsPrimaryKey() && (kind == ConfigChangeKind.Created || p.IsModified))
					.Select(p => p.Metadata.Name)
					.ToArray();
				return new ConfigChange(e.Metadata.ClrType, entity.Id,
					kind == ConfigChangeKind.Deleted ? null : entity, kind,
					kind == ConfigChangeKind.Deleted ? [] : changed);
			})
			.ToList();
	}

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Every IConfigEntity in this assembly becomes a table, no registration needed.
		var entityTypes = typeof(AstroDbContext).Assembly.GetTypes()
			.Where(t => typeof(IConfigEntity).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

		foreach (var type in entityTypes)
		{
			var entity = modelBuilder.Entity(type);
			entity.HasKey(nameof(IConfigEntity.Id));
			entity.Property(nameof(IConfigEntity.Id)).ValueGeneratedNever();
			ApplyInitializerDefaults(modelBuilder, type);
		}
	}

	/// <summary>
	/// Makes property initializers (e.g. <c>public bool Enabled { get; set; } = true;</c>) also become the column default,
	/// so existing rows get that value when a new column is added instead of false/0.
	/// </summary>
	private static void ApplyInitializerDefaults(ModelBuilder modelBuilder, Type type)
	{
		var template = Activator.CreateInstance(type);
		var entityType = modelBuilder.Entity(type).Metadata;

		foreach (var property in entityType.GetProperties())
		{
			if (property.IsPrimaryKey() || property.IsPrimitiveCollection || property.PropertyInfo is null) continue;

			var value = property.PropertyInfo.GetValue(template);
			var clrDefault = property.ClrType.IsValueType ? Activator.CreateInstance(property.ClrType) : null;
			if (value is null || Equals(value, clrDefault)) continue;

			// Sentinel == default means "if the value equals the default, let the DB fill it in", which yields the same value.
			modelBuilder.Entity(type).Property(property.Name).HasDefaultValue(value).HasSentinel(value);
		}
	}
}

/// <summary>
/// Used by the dotnet-ef CLI (Tools/migrate.sh) to build the context without running the bot.
/// </summary>
public sealed class AstroDbContextFactory : IDesignTimeDbContextFactory<AstroDbContext>
{
	public AstroDbContext CreateDbContext(string[] args) => AstroDb.Create();
}
