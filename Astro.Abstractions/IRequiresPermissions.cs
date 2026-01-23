using Discord;

namespace Astro.Abstractions;

public interface IRequiresPermissions
{
	public List<GuildPermission> RequiredPermissions { get; }
	public bool RequiresWhitelist { get; }
	public bool HideIfCantExecute { get; }
	public string RejectMessage { get; }
}