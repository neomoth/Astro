using Discord;
using Discord.WebSocket;
using JetBrains.Annotations;

namespace Astro.Abstractions;

public interface ICommand : IDisposable
{
	public string Name { get; }
	public string Description { get; }
	public bool IsNsfw { get; }
	public bool HideIfCantExecute { get; }
	public List<SlashCommandOptionBuilder> Options { get; }
	public List<ICommand> SubCommands { get; }
	/// <summary>
	/// The command's own <see cref="RequirePermissionsAttribute"/>, or null if it inherits its parent's.
	/// </summary>
	public RequirePermissionsAttribute? DeclaredRequirements { get; }
	/// <summary>
	/// Where the command's own <see cref="AvailableInAttribute"/> allows it to run, or null if it inherits its parent's.
	/// </summary>
	public CommandLocation? DeclaredLocations { get; }
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

[MeansImplicitUse]
[AttributeUsage(AttributeTargets.Class)]
public sealed class RootCommandAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CommandGroupAttribute(Type parent) : Attribute
{
	public Type Parent => parent;
}

/// <summary>
/// Registers this command under <paramref name="parent"/>. Subcommands are instantiated via reflection,
/// so classes marked with this are treated as used (like [UsedImplicitly]).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
[MeansImplicitUse(ImplicitUseKindFlags.InstantiatedNoFixedConstructorSignature)]
public sealed class SubCommandAttribute(Type parent) : Attribute
{
	public Type Parent => parent;
}