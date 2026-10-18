using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Astro.Abstractions;
using Astro.Bot.Configuration;
using Astro.Bot.Security;
using Astro.DB;
using Astro.DB.Entities;
using IoC;
using Microsoft.EntityFrameworkCore;
using RTLogging;

namespace Astro.Bot.OAuth;

/// <summary>
/// Discord OAuth2 (authorization code flow). Users get a link from <see cref="CreateAuthorizationUrl"/>, authorize in
/// their browser, and Discord sends them back to <see cref="RedirectUri"/>, which this manager serves with a small
/// HTTP listener. Their tokens are stored in <see cref="OAuthToken"/> and refreshed by <see cref="GetAccessToken"/>.
/// Tokens are encrypted at rest with <see cref="SecretProtector"/>; everything this manager returns is decrypted.
/// </summary>
/// <remarks>
/// Configured under <c>Bot.OAuth</c>: <c>Enabled</c>, <c>ListenUrl</c> (what the listener binds to),
/// <c>PublicUrl</c> (what browsers reach, e.g. through a reverse proxy) and <c>Scopes</c>. Needs <c>Bot.ClientSecret</c>.
/// The redirect URI must also be added in the Developer Portal under OAuth2 → Redirects. Changes apply on restart.
/// </remarks>
public sealed class OAuthManager : Manager, IDisposable
{
	private const string ApiBase = "https://discord.com/api/v10";
	private const string CallbackPath = "/oauth/callback";
	private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

	[Dependency] private readonly GlobalConfigManager _config = null!;
	[Dependency] private readonly AstroBot _bot = null!;
	[Dependency] private readonly SecretProtector _secrets = null!;

	private readonly HttpClient _http = new();
	private readonly ConcurrentDictionary<string, PendingAuthorization> _pending = [];
	private readonly SemaphoreSlim _refreshLock = new(1, 1);
	private HttpListener? _listener;
	private CancellationTokenSource? _cts;

	protected override string SawmillName => "astro.oauth";

	/// <summary>Raised after a user authorizes and their tokens are stored. Hook features that need OAuth in here.</summary>
	public event Func<ulong, OAuthToken, Task>? Authorized;

	public bool IsRunning => _listener?.IsListening == true;

	public string RedirectUri =>
		$"{(_config.Registry.Get<string>("Bot.OAuth.PublicUrl") ?? string.Empty).TrimEnd('/')}{CallbackPath}";

	/// <summary>
	/// Scopes that make an authorize link install the app instead of linking an account. Installing is done through the
	/// Developer Portal's install links, so these are left out of <see cref="Scopes"/>.
	/// </summary>
	private static readonly string[] InstallScopes = ["applications.commands", "bot", "webhook.incoming"];

	/// <summary>The scopes <c>/oauth link</c> asks for: <c>Bot.OAuth.Scopes</c>, minus <see cref="InstallScopes"/>.</summary>
	public string[] Scopes =>
		_configuredScopes.Except(InstallScopes, StringComparer.OrdinalIgnoreCase).DefaultIfEmpty("identify").ToArray();

	private string[] _configuredScopes => _config.Registry.Get<string[]>("Bot.OAuth.Scopes") ?? [];

	private string _clientSecret => _config.Registry.Get<string>("Bot.ClientSecret") ?? string.Empty;

	// Bot user IDs match their application's ID, so this works without Bot.ClientId being filled in.
	private ulong _clientId => _config.Registry.Get<ulong>("Bot.ClientId") is not 0 and var id
		? id
		: _bot.Client.CurrentUser?.Id ??
		  throw new InvalidOperationException("Bot.ClientId isn't set and the bot isn't logged in yet.");

	public void Start()
	{
		if (!_config.Registry.Get<bool>("Bot.OAuth.Enabled"))
		{
			Log.Verbose("OAuth is disabled (Bot.OAuth.Enabled).");
			return;
		}

		if (string.IsNullOrWhiteSpace(_clientSecret))
		{
			Log.Warn("OAuth is enabled, but Bot.ClientSecret is empty. Not starting.");
			return;
		}

		var prefix = _config.Registry.Get<string>("Bot.OAuth.ListenUrl") ?? "http://localhost:5000/";
		if (!prefix.EndsWith('/')) prefix += '/';

		var listener = new HttpListener();
		listener.Prefixes.Add(prefix);
		try
		{
			listener.Start();
		}
		catch (Exception e) when (e is HttpListenerException or ArgumentException)
		{
			Log.Log(LogLevel.Error, e, "Couldn't listen on {prefix} for OAuth callbacks.", prefix);
			listener.Close();
			return;
		}

		_listener = listener;
		_cts = new CancellationTokenSource();
		_ = Task.Run(() => Listen(listener, _cts.Token));
		Log.Info("Listening for OAuth callbacks on {prefix}. Redirect URI: {uri}", prefix, RedirectUri);

		var ignored = _configuredScopes.Intersect(InstallScopes, StringComparer.OrdinalIgnoreCase).ToArray();
		if (ignored.Length > 0)
			Log.Warn("Ignoring {scopes:l} from Bot.OAuth.Scopes: they turn account links into app installs. " +
			         "Install the app with the Developer Portal's install link instead.", string.Join(", ", ignored));
	}

	public void Stop()
	{
		_cts?.Cancel();
		_listener?.Close();
		_listener = null;
	}

	public void Dispose()
	{
		Stop();
		_http.Dispose();
		_refreshLock.Dispose();
	}

	/// <summary>
	/// A link that installs the app: to the user's account (<paramref name="userInstall"/>), so its commands work
	/// anywhere they are, or to a server they pick. Unlike <see cref="CreateAuthorizationUrl"/>, it doesn't need the
	/// OAuth listener or a redirect, and works for anyone.
	/// </summary>
	public string CreateInstallUrl(bool userInstall) => "https://discord.com/oauth2/authorize?" + Query(
		("client_id", _clientId.ToString()),
		("integration_type", userInstall ? "1" : "0"),
		("scope", userInstall ? "applications.commands" : "bot applications.commands"));

	/// <summary>
	/// A one-time link for <paramref name="userId"/> to authorize the bot, valid for <see cref="StateLifetime"/>.
	/// Only that user can complete it; authorizing with another account is rejected.
	/// </summary>
	public string CreateAuthorizationUrl(ulong userId)
	{
		foreach (var (key, _) in _pending.Where(p => p.Value.ExpiresAt < DateTime.UtcNow))
			_pending.TryRemove(key, out _);

		var state = Base64Url(RandomNumberGenerator.GetBytes(32));
		_pending[state] = new PendingAuthorization(userId, DateTime.UtcNow + StateLifetime);

		return "https://discord.com/oauth2/authorize?" + Query(
			("response_type", "code"),
			("client_id", _clientId.ToString()),
			("scope", string.Join(' ', Scopes)),
			("state", state),
			("redirect_uri", RedirectUri),
			("prompt", "consent"));
	}

	public async Task<bool> IsLinked(ulong userId) => await Find(userId) is not null;

	/// <summary>The scopes <paramref name="userId"/> granted, or null if they haven't linked their account.</summary>
	public async Task<string[]?> GetGrantedScopes(ulong userId) =>
		(await Find(userId))?.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>
	/// A working access token for <paramref name="userId"/>, refreshing it first if it's expired. Null if they haven't
	/// linked their account, or if they revoked access (their stored tokens are removed in that case).
	/// </summary>
	public async Task<string?> GetAccessToken(ulong userId)
	{
		var token = await Find(userId);
		if (token is null) return null;
		if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(1)) return token.AccessToken;

		await _refreshLock.WaitAsync();
		try
		{
			// Someone else may have refreshed it while we waited.
			token = await Find(userId);
			if (token is null) return null;
			if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(1)) return token.AccessToken;

			try
			{
				var refreshed = await RequestToken(("grant_type", "refresh_token"),
					("refresh_token", token.RefreshToken));
				return (await Store(userId, refreshed)).AccessToken;
			}
			catch (OAuthException e) when (e.Status is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
			{
				Log.Info("Refreshing {id}'s OAuth token failed, so they likely revoked access. Removing it.", userId);
				await AstroDb.Delete<OAuthToken>(userId);
				return null;
			}
		}
		finally
		{
			_refreshLock.Release();
		}
	}

	/// <summary>Revokes the user's tokens with Discord and deletes them. False if they weren't linked.</summary>
	public async Task<bool> Unlink(ulong userId)
	{
		var token = await Find(userId);
		if (token is null) return false;

		await Revoke(token.RefreshToken, "refresh_token");
		await AstroDb.Delete<OAuthToken>(userId);
		Log.Info("{id} unlinked their account.", userId);
		return true;
	}

	private async Task Listen(HttpListener listener, CancellationToken cancel)
	{
		while (!cancel.IsCancellationRequested)
		{
			HttpListenerContext context;
			try
			{
				context = await listener.GetContextAsync();
			}
			catch (Exception) when (cancel.IsCancellationRequested || !listener.IsListening)
			{
				break;
			}

			_ = Task.Run(() => HandleRequest(context), cancel);
		}
	}

	private async Task HandleRequest(HttpListenerContext context)
	{
		try
		{
			// EndsWith rather than equals, so a reverse proxy can serve this under a sub-path.
			var path = context.Request.Url?.AbsolutePath.TrimEnd('/') ?? string.Empty;
			if (context.Request.HttpMethod != "GET" || !path.EndsWith(CallbackPath, StringComparison.OrdinalIgnoreCase))
			{
				await RespondPage(context, HttpStatusCode.NotFound, "Not found", "There's nothing here.");
				return;
			}

			var query = context.Request.QueryString;
			var state = query["state"];
			if (state is null || !_pending.TryRemove(state, out var pending) || pending.ExpiresAt < DateTime.UtcNow)
			{
				await RespondPage(context, HttpStatusCode.BadRequest, "Link expired",
					"This link is invalid or has expired. Run /oauth link in Discord to get a new one.");
				return;
			}

			if (query["error"] is not null || query["code"] is not { } code)
			{
				await RespondPage(context, HttpStatusCode.BadRequest, "Authorization cancelled",
					"Nothing was linked. You can close this tab.");
				return;
			}

			var tokens = await RequestToken(("grant_type", "authorization_code"), ("code", code),
				("redirect_uri", RedirectUri));

			// The state proves who asked for the link; this proves who actually authorized.
			var authorizedId = await GetUserId(tokens.AccessToken);
			if (authorizedId != pending.UserId)
			{
				await Revoke(tokens.AccessToken, "access_token");
				await RespondPage(context, HttpStatusCode.Forbidden, "Wrong account",
					"You authorized with a different Discord account than the one that asked for this link. Nothing was linked.");
				return;
			}

			var stored = await Store(pending.UserId, tokens);
			Log.Info("{id} linked their account (scopes: {scopes:l}).", pending.UserId, stored.Scopes);
			await RespondPage(context, HttpStatusCode.OK, "Account linked",
				"You're all set. You can close this tab and head back to Discord.");

			foreach (var handler in Authorized?.GetInvocationList().Cast<Func<ulong, OAuthToken, Task>>() ?? [])
			{
				try
				{
					await handler(pending.UserId, stored);
				}
				catch (Exception e)
				{
					Log.Log(LogLevel.Error, e, "An OAuth Authorized handler threw.");
				}
			}
		}
		catch (Exception e)
		{
			Log.Log(LogLevel.Error, e, "Handling an OAuth callback failed.");
			try
			{
				await RespondPage(context, HttpStatusCode.InternalServerError, "Something went wrong",
					"Linking failed on our end. Try again in a bit.");
			}
			catch
			{
				// The response may already be sent or the connection gone; nothing more to do.
			}
		}
	}

	/// <summary>
	/// The user's tokens, decrypted. Rows that can't be decrypted (e.g. the secret key changed) are deleted, since
	/// they're useless; the user just has to link again. Plaintext rows from before encryption are encrypted in place.
	/// </summary>
	private async Task<OAuthToken?> Find(ulong userId)
	{
		var row = await AstroDb.Query(db => db.OAuthTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Id == userId));
		if (row is null) return null;

		if (!SecretProtector.IsProtected(row.AccessToken) || !SecretProtector.IsProtected(row.RefreshToken))
		{
			Log.Info("Encrypting {id}'s OAuth tokens, which were stored in plaintext.", userId);
			return await Save(userId, row.AccessToken, row.RefreshToken, row.ExpiresAt, row.Scopes);
		}

		try
		{
			return new OAuthToken
			{
				Id = row.Id,
				AccessToken = _secrets.Unprotect(row.AccessToken, Context(userId, "access")),
				RefreshToken = _secrets.Unprotect(row.RefreshToken, Context(userId, "refresh")),
				ExpiresAt = row.ExpiresAt,
				Scopes = row.Scopes,
			};
		}
		catch (CryptographicException e)
		{
			Log.Log(LogLevel.Warn, e,
				"Couldn't decrypt {id}'s OAuth tokens, so they're being removed. They'll need to link again.", userId);
			await AstroDb.Delete<OAuthToken>(userId);
			return null;
		}
	}

	private Task<OAuthToken> Store(ulong userId, TokenResponse tokens) =>
		Save(userId, tokens.AccessToken, tokens.RefreshToken, DateTime.UtcNow.AddSeconds(tokens.ExpiresIn),
			tokens.Scope);

	/// <summary>Encrypts and saves the tokens, returning them decrypted.</summary>
	private async Task<OAuthToken> Save(ulong userId, string accessToken, string refreshToken, DateTime expiresAt,
		string scopes)
	{
		await AstroDb.Update<OAuthToken>(userId, t =>
		{
			t.AccessToken = _secrets.Protect(accessToken, Context(userId, "access"));
			t.RefreshToken = _secrets.Protect(refreshToken, Context(userId, "refresh"));
			t.ExpiresAt = expiresAt;
			t.Scopes = scopes;
		});

		return new OAuthToken
		{
			Id = userId,
			AccessToken = accessToken,
			RefreshToken = refreshToken,
			ExpiresAt = expiresAt,
			Scopes = scopes
		};
	}

	private static string Context(ulong userId, string field) => $"oauth:{userId}:{field}";

	private async Task<TokenResponse> RequestToken(params (string Key, string Value)[] form)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/oauth2/token");
		request.Headers.Authorization = ClientAuth();
		request.Content = new FormUrlEncodedContent(form.Select(f => KeyValuePair.Create(f.Key, f.Value)));

		using var response = await _http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
			throw new OAuthException(response.StatusCode, body);

		return JsonSerializer.Deserialize<TokenResponse>(body)
		       ?? throw new OAuthException(response.StatusCode, "Empty token response.");
	}

	private async Task<ulong> GetUserId(string accessToken)
	{
		using var request = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/users/@me");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

		using var response = await _http.SendAsync(request);
		var body = await response.Content.ReadAsStringAsync();
		if (!response.IsSuccessStatusCode)
			throw new OAuthException(response.StatusCode, body);

		using var json = JsonDocument.Parse(body);
		return ulong.Parse(json.RootElement.GetProperty("id").GetString()!);
	}

	/// <summary>Best effort: a failure here only means Discord keeps the token until it expires.</summary>
	private async Task Revoke(string token, string tokenType)
	{
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/oauth2/token/revoke");
			request.Headers.Authorization = ClientAuth();
			request.Content = new FormUrlEncodedContent([
				KeyValuePair.Create("token", token), KeyValuePair.Create("token_type_hint", tokenType)
			]);
			using var response = await _http.SendAsync(request);
			if (!response.IsSuccessStatusCode)
				Log.Warn("Revoking an OAuth token failed with {status}.", (int)response.StatusCode);
		}
		catch (HttpRequestException e)
		{
			Log.Log(LogLevel.Warn, e, "Revoking an OAuth token failed.");
		}
	}

	private AuthenticationHeaderValue ClientAuth() =>
		new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));

	private static async Task RespondPage(HttpListenerContext context, HttpStatusCode status, string title,
		string message)
	{
		var html = $"""
		            <!doctype html>
		            <html lang="en">
		            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{WebUtility.HtmlEncode(title)}</title></head>
		            <body style="font-family: system-ui, sans-serif; max-width: 32rem; margin: 4rem auto; padding: 0 1rem;">
		            <h1>{WebUtility.HtmlEncode(title)}</h1>
		            <p>{WebUtility.HtmlEncode(message)}</p>
		            </body>
		            </html>
		            """;
		var bytes = Encoding.UTF8.GetBytes(html);
		context.Response.StatusCode = (int)status;
		context.Response.ContentType = "text/html; charset=utf-8";
		context.Response.ContentLength64 = bytes.Length;
		await context.Response.OutputStream.WriteAsync(bytes);
		context.Response.Close();
	}

	private static string Query(params (string Key, string Value)[] pairs) =>
		string.Join('&', pairs.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

	private static string Base64Url(byte[] bytes) =>
		Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private sealed record PendingAuthorization(ulong UserId, DateTime ExpiresAt);

	private sealed record TokenResponse(
		[property: JsonPropertyName("access_token")]
		string AccessToken,
		[property: JsonPropertyName("refresh_token")]
		string RefreshToken,
		[property: JsonPropertyName("expires_in")]
		int ExpiresIn,
		[property: JsonPropertyName("scope")] string Scope);
}

/// <summary>Discord rejected an OAuth request.</summary>
public sealed class OAuthException(HttpStatusCode status, string body)
	: Exception($"Discord OAuth request failed with {(int)status}: {body}")
{
	public HttpStatusCode Status => status;
}
