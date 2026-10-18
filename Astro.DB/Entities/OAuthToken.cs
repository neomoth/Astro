namespace Astro.DB.Entities;

/// <summary>
/// OAuth2 tokens a user granted the bot, keyed by their user ID. Managed by the bot's OAuthManager; get a usable
/// access token through it rather than reading this directly, since it refreshes expired ones.
/// </summary>
/// <remarks>
/// <see cref="AccessToken"/> and <see cref="RefreshToken"/> are stored encrypted (by the bot's SecretProtector), so
/// the values in the database aren't usable as-is.
/// </remarks>
public sealed class OAuthToken : IConfigEntity
{
	public ulong Id { get; set; }

	public string AccessToken { get; set; } = string.Empty;
	public string RefreshToken { get; set; } = string.Empty;

	/// <summary>When <see cref="AccessToken"/> stops working, in UTC.</summary>
	public DateTime ExpiresAt { get; set; }

	/// <summary>The scopes the user actually granted, space separated.</summary>
	public string Scopes { get; set; } = string.Empty;
}
