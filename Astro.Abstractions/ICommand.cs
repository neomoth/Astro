using Discord;
using Discord.WebSocket;
using Serilog;

namespace Astro.Abstractions;

public interface ICommand : IDisposable
{
	public string Name { get; }
	public string Description { get; }
	public bool IsNsfw { get; }
	public bool HideIfCantExecute { get; }
	public List<SlashCommandOptionBuilder> Options { get; }
	public List<ICommand> SubCommands { get; }
	public List<GuildPermission> RequiredPermissions { get; }
	public bool RequiresWhitelist { get; }
	public string RejectMessage { get; }
	public Task ExecuteCommand(SocketSlashCommand command, ICommandArgs commandArgs);
}

public interface ICommandGroup : ICommand
{
}

/// <summary>
/// Shell interface in order to facilitate properly passing command arguments to the command considering its structure is defined here
/// </summary>
public interface ICommandArgs
{
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class RootCommandAttribute : Attribute{}

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandGroupAttribute(Type parent) : Attribute
{
	public Type Parent => parent;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class SubCommandAttribute(Type parent) : Attribute
{
	public Type Parent => parent;
}