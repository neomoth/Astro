using Astro.Abstractions;
using Astro.Bot.Permissions;
using Discord;
using Discord.WebSocket;
using IoC;

namespace Astro.Bot.Commands;

[RootCommand]
[RequirePermissions(Global = UserPermissions.Host)]
public sealed class GlobalPermissionsCommand : Command
{
	[Dependency] private readonly AstroBot _bot = null!;
	[Dependency] private readonly HostManager _host = null!;
	[Dependency] private readonly UserPermissionManager _perms = null!;

	public override string Name => "globalpermissions";
	public override string Description => "Set global permissions for a user in the database.";
	public override bool HideIfCantExecute => true;

	public override List<SlashCommandOptionBuilder> Options =>
	[
		new SlashCommandOptionBuilder().WithName("user").WithDescription("The user to set flags for.")
			.WithType(ApplicationCommandOptionType.User).WithRequired(true),
		new SlashCommandOptionBuilder().WithName("flags").WithDescription("The new global permission flags for this user.")
			.WithType(ApplicationCommandOptionType.String)
	];

	protected override async Task ExecuteCommand(SocketSlashCommand command, CommandArgs args)
	{
		await command.DeferAsync(ephemeral: true);

		var user = args.Get<IUser>("user");
		var flagsString = args.Get<string>("flags");

		if (user is null || string.IsNullOrWhiteSpace(flagsString))
		{
			await command.FollowupAsync("User and flags parameters are required. Try again with those specified.",
				ephemeral: true);
			return;
		}

		if (_host.IsHost(user.Id))
		{
			await command.FollowupAsync("Cannot edit your own flags as host.", ephemeral: true);
			return;
		}

		if (user.Id == _bot.Client.CurrentUser.Id)
		{
			await command.FollowupAsync("Why are you trying to edit my flags?", ephemeral: true);
			return;
		}

		if (!_perms.TryParseFlagString(flagsString, out var flags))
		{
			await command.FollowupAsync(
				"Error setting flags, likely a parsing issue. Check input string and try again.", ephemeral: true);
			return;
		}

		await _perms.Set(user.Id, flags);
		await command.FollowupAsync($"Updated global flags for {user.Username}.", ephemeral: true);
	}
}
