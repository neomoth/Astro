using Astro.Abstractions;
using Astro.Bot.Configuration;
using Astro.DB;
using Astro.DB.Entities;
using IoC;
using Microsoft.EntityFrameworkCore;
using RTLogging;

namespace Astro.Bot.Permissions;

/// <summary>
/// Keeps <see cref="UserConfig.Permissions"/> in the database in line with <c>Bot.Hosts.Users</c>: every host is granted
/// <see cref="UserPermissions.All"/>, and anyone else holding <see cref="UserPermissions.Host"/> is reset to
/// <see cref="UserPermissions.None"/>, since there's no record of which flags they had before becoming a host.
/// </summary>
public sealed class HostManager : Manager
{
	[Dependency] private readonly GlobalConfigManager _config = null!;
	[Dependency] private readonly UserPermissionManager _permissions = null!;

	private readonly SemaphoreSlim _syncLock = new(1, 1);

	protected override string SawmillName => "astro.hosts";

	/// <summary>
	/// Syncs once, then again whenever the config is saved. Call after the database and config are both loaded.
	/// </summary>
	public override void Initialize() => _ = InternalInit();

	private async Task InternalInit()
	{
		await SyncHosts();
		_config.Saved += OnConfigSaved;
	}

	public bool IsHost(ulong id, bool ignoreSpoof = false) => _permissions.Has(id, UserPermissions.Host, ignoreSpoof);

	private void OnConfigSaved() => _ = Task.Run(async () =>
	{
		try
		{
			await SyncHosts();
		}
		catch (Exception e)
		{
			Log.Log(LogLevel.Error, e, "Failed to sync host permissions.");
		}
	});

	private async Task SyncHosts()
	{
		var hosts = (_config.Registry.Get<ulong[]>("Bot.Hosts.Users") ?? []).ToHashSet();

		await _syncLock.WaitAsync();
		try
		{
			await AstroDb.Run(async db =>
			{
				// The user table only holds users with custom config, so pulling it in whole is fine.
				var users = await db.Users.ToDictionaryAsync(u => u.Id);

				foreach (var id in hosts)
				{
					if (!users.TryGetValue(id, out var user))
					{
						db.Users.Add(new UserConfig { Id = id, Permissions = UserPermissions.All });
						Log.Info("Granted host {id} all permissions.", id);
						continue;
					}

					if (user.Permissions == UserPermissions.All) continue;
					user.Permissions = UserPermissions.All;
					Log.Info("Granted host {id} all permissions.", id);
				}

				foreach (var user in users.Values.Where(u =>
					         !hosts.Contains(u.Id) && u.Permissions.HasFlag(UserPermissions.Host)))
				{
					user.Permissions = UserPermissions.None;
					Log.Info("Revoked all permissions from {id}, who is no longer a host.", user.Id);
				}
			});
			await _permissions.Reload();
		}
		finally
		{
			_syncLock.Release();
		}
	}
}
