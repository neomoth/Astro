using System.Collections.Concurrent;
using Astro.Abstractions;
using Astro.DB;
using Microsoft.EntityFrameworkCore;

namespace Astro.Bot.Permissions;

/// <summary>
/// In-memory copy of every user's <see cref="UserPermissions"/>, so permission checks don't need to hit the database.
/// Change permissions through <see cref="Set"/> (or call <see cref="Reload"/> after writing to the database directly)
/// so the cache stays accurate.
/// <para/>
/// Users can also spoof having no global permissions (for testing), which makes <see cref="Get"/> and
/// <see cref="Has"/> report <see cref="UserPermissions.None"/> for them. Spoofing is kept in memory only, so it ends on
/// restart, and it never touches what's stored in the database.
/// </summary>
public sealed class UserPermissionManager : Manager
{
	private readonly ConcurrentDictionary<ulong, UserPermissions> _permissions = [];
	private readonly ConcurrentDictionary<ulong, byte> _spoofing = [];

	protected override string SawmillName => "astro.permissions";

	/// <summary>The user's permissions, or <see cref="UserPermissions.None"/> while they're spoofing.</summary>
	public UserPermissions Get(ulong userId, bool ignoreSpoof = false) =>
		!ignoreSpoof && IsSpoofing(userId) ? UserPermissions.None : _permissions.GetValueOrDefault(userId);

	/// <summary>Whether the user holds every flag in <paramref name="required"/>. Hosts hold every flag.</summary>
	public bool Has(ulong userId, UserPermissions required, bool ignoreSpoof = false)
	{
		var held = Get(userId, ignoreSpoof);
		return held.HasFlag(UserPermissions.Host) || held.HasFlag(required);
	}

	public bool IsSpoofing(ulong userId) => _spoofing.ContainsKey(userId);

	public void SetSpoofing(ulong userId, bool spoof)
	{
		if (spoof) _spoofing[userId] = 0;
		else _spoofing.TryRemove(userId, out _);
		Log.Info("{id} {action:l} spoofing having no global permissions.", userId, spoof ? "started" : "stopped");
	}

	public async Task Set(ulong userId, UserPermissions value)
	{
		await AstroDb.UpdateUser(userId, u => u.Permissions = value);
		_permissions[userId] = value;
	}

	public async Task Reload()
	{
		var users = await AstroDb.Query(db => db.Users.AsNoTracking().ToListAsync());
		_permissions.Clear();
		foreach (var user in users)
			_permissions[user.Id] = user.Permissions;
		Log.Verbose("Loaded permissions for {count} users.", users.Count);
	}

	/// <summary>
	/// Parses flag names separated by <c>|</c> or <c>,</c> (e.g. <c>"GlobalModerator|GlobalAdmin|Danger"</c>) into
	/// <see cref="UserPermissions"/>. Names are case-insensitive, raw numbers are allowed, and an empty string is
	/// <see cref="UserPermissions.None"/>.
	/// </summary>
	/// <exception cref="FormatException">One or more names aren't <see cref="UserPermissions"/> flags.</exception>
	public UserPermissions ParseFlagString(string input)
	{
		var result = UserPermissions.None;
		var invalid = new List<string>();

		foreach (var part in input.Split(['|', ','],
			         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (Enum.TryParse<UserPermissions>(part, ignoreCase: true, out var flag))
				result |= flag;
			else
				invalid.Add(part);
		}

		if (invalid.Count > 0)
			throw new FormatException(
				$"Unknown permission flag(s): {string.Join(", ", invalid)}. " +
				$"Valid flags: {string.Join(", ", Enum.GetNames<UserPermissions>())}.");

		return result;
	}

	/// <summary>
	/// Like <see cref="ParseFlagString"/>, but returns false instead of throwing. <paramref name="permissions"/> is
	/// <see cref="UserPermissions.None"/> when parsing fails.
	/// </summary>
	public bool TryParseFlagString(string input, out UserPermissions permissions)
	{
		try
		{
			permissions = ParseFlagString(input);
			return true;
		}
		catch (FormatException e)
		{
			Log.Verbose(e.Message);
			permissions = UserPermissions.None;
			return false;
		}
	}
}
