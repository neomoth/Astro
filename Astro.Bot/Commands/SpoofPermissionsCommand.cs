using Astro.Abstractions;
using Astro.Bot.Permissions;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

/// <summary>
/// Lets global admins see the bot the way a user without global permissions would, for testing.
/// </summary>
[RootCommand]
[RequirePermissions(Global = UserPermissions.GlobalAdmin, IgnoreSpoof = true)]
public sealed class SpoofPermissionsCommand : Command
{
	[Dependency] private readonly UserPermissionManager _permissions = null!;

	public override string Name => "spoofpermissions";
	public override string Description => "Act as if you had no global permissions, for testing. Run again to stop.";
	public override bool HideIfCantExecute => true;

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("enabled")
			.WithDescription("Whether to spoof. Toggles if left out.")
			.WithType(ApplicationCommandOptionType.Boolean)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		var userId = command.User.Id;
		var spoof = args.Get<bool?>("enabled") ?? !_permissions.IsSpoofing(userId);
		_permissions.SetSpoofing(userId, spoof);

		await command.RespondAsync(spoof
				? "You now have no global permissions, until you run `/spoofpermissions` again or the bot restarts. " +
				  "Guild permissions are unaffected."
				: "Spoofing stopped. Your global permissions are back.",
			ephemeral: true);
	}
}
