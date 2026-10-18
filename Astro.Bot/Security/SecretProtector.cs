using System.Security.Cryptography;
using System.Text;
using Astro.Abstractions;

namespace Astro.Bot.Security;

/// <summary>
/// Encrypts secrets for storage (e.g. OAuth tokens in the database) with AES-256-GCM, so a copy of the database alone
/// doesn't reveal them.
/// </summary>
/// <remarks>
/// The key comes from the <c>ASTRO_SECRET_KEY</c> environment variable (32 bytes, base64) if it's set, otherwise from
/// <see cref="KeyPath"/>, which is generated on first run. Keep it out of the database's backups, but do back it up:
/// without it, everything it protected is unreadable.
/// <para/>
/// Every value is bound to a <c>context</c> string (e.g. <c>oauth:123:access</c>), so an encrypted value only decrypts
/// where it was stored and can't be moved to another row or field.
/// </remarks>
public sealed class SecretProtector : Manager
{
	private const string Prefix = "v1:";
	private const string KeyEnvironmentVariable = "ASTRO_SECRET_KEY";
	private const int KeySize = 32;
	private const int NonceSize = 12;
	private const int TagSize = 16;

	private byte[] _key = null!;

	protected override string SawmillName => "astro.secrets";

	public static string KeyPath => Path.Combine(ResourcePaths.Keys, "secrets.key");

	public override void PostInject() => _key = LoadKey();

	/// <summary>Whether <paramref name="value"/> was produced by <see cref="Protect"/>, rather than being plaintext.</summary>
	public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

	public string Protect(string plaintext, string context)
	{
		var nonce = RandomNumberGenerator.GetBytes(NonceSize);
		var input = Encoding.UTF8.GetBytes(plaintext);
		var output = new byte[NonceSize + input.Length + TagSize];
		var ciphertext = output.AsSpan(NonceSize, input.Length);
		var tag = output.AsSpan(NonceSize + input.Length, TagSize);

		using var aes = new AesGcm(_key, TagSize);
		aes.Encrypt(nonce, input, ciphertext, tag, Encoding.UTF8.GetBytes(context));
		nonce.CopyTo(output, 0);

		return Prefix + Convert.ToBase64String(output);
	}

	/// <exception cref="CryptographicException">
	/// The value wasn't made with this key or context, or was tampered with.
	/// </exception>
	public string Unprotect(string stored, string context)
	{
		if (!IsProtected(stored))
			throw new CryptographicException("Value isn't encrypted.");

		byte[] data;
		try
		{
			data = Convert.FromBase64String(stored[Prefix.Length..]);
		}
		catch (FormatException e)
		{
			throw new CryptographicException("Value isn't valid base64.", e);
		}

		if (data.Length < NonceSize + TagSize)
			throw new CryptographicException("Value is too short.");

		var plaintext = new byte[data.Length - NonceSize - TagSize];
		using var aes = new AesGcm(_key, TagSize);
		aes.Decrypt(
			data.AsSpan(0, NonceSize),
			data.AsSpan(NonceSize, plaintext.Length),
			data.AsSpan(NonceSize + plaintext.Length, TagSize),
			plaintext,
			Encoding.UTF8.GetBytes(context));

		return Encoding.UTF8.GetString(plaintext);
	}

	private byte[] LoadKey()
	{
		if (Environment.GetEnvironmentVariable(KeyEnvironmentVariable) is { Length: > 0 } fromEnvironment)
		{
			Log.Verbose("Using the secret key from {var}.", KeyEnvironmentVariable);
			return Decode(fromEnvironment, KeyEnvironmentVariable);
		}

		if (File.Exists(KeyPath))
			return Decode(File.ReadAllText(KeyPath), KeyPath);

		var key = RandomNumberGenerator.GetBytes(KeySize);
		Directory.CreateDirectory(ResourcePaths.Keys);
		var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
		if (!OperatingSystem.IsWindows())
			options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
		using (var writer = new StreamWriter(KeyPath, Encoding.ASCII, options))
			writer.Write(Convert.ToBase64String(key));

		Log.Info("Generated a new secret key at {path}. Back it up: without it, encrypted secrets can't be read.", KeyPath);
		return key;
	}

	private static byte[] Decode(string base64, string source)
	{
		byte[] key;
		try
		{
			key = Convert.FromBase64String(base64.Trim());
		}
		catch (FormatException e)
		{
			throw new InvalidOperationException($"The secret key in {source} isn't valid base64.", e);
		}

		return key.Length == KeySize
			? key
			: throw new InvalidOperationException($"The secret key in {source} must be {KeySize} bytes, not {key.Length}.");
	}
}
