namespace Astro.Abstractions;

/// <summary>
/// Restricts editing a server or user config property through <c>/config</c>. Put it on properties that are normally
/// changed by people holding certain global flags, so others can't change them on themselves or their server.
/// </summary>
/// <example>
/// <code>
/// // Nobody can edit it through /config (it's managed by a dedicated command).
/// [ConfigRestricted]
/// public UserPermissions Permissions { get; set; }
///
/// // Only users holding Danger can.
/// [ConfigRestricted(UserPermissions.Danger)]
/// public bool SomethingRisky { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ConfigRestrictedAttribute : Attribute
{
	/// <summary>Not editable through <c>/config</c> by anyone, hosts included.</summary>
	public ConfigRestrictedAttribute() => Required = null;

	/// <summary>Editable through <c>/config</c> only by users holding every flag in <paramref name="required"/>.</summary>
	public ConfigRestrictedAttribute(UserPermissions required) => Required = required;

	/// <summary>The flags needed to edit it, or null if it can't be edited through <c>/config</c> at all.</summary>
	public UserPermissions? Required { get; }
}
