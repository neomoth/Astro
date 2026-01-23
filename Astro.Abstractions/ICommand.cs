using Discord;
using Discord.WebSocket;
using Serilog;

namespace Astro.Abstractions;

public interface IBaseCommandMetadata
{
	public string Name { get; }
	public string Description { get; }
}

public interface IExecutableCommand : IBaseCommandMetadata
{
	public Task ExecuteCommand(SocketSlashCommand command);
	public List<SlashCommandOptionBuilder> Options { get; }
	public List<ISubCommand> SubCommands { get; }
}

public interface ICommand : IDisposable, IRequiresPermissions, IExecutableCommand
{
	public bool IsNsfw { get; }
}

public interface ISubCommand : IDisposable, IRequiresPermissions, IExecutableCommand
{
	public SlashCommandOptionBuilder Build();
}