namespace Astro.Bot;

/// <summary>
/// Startup can't continue for a reason that's already been logged (e.g. the bot token is missing). The bot exits with
/// code 1 instead of printing a stack trace.
/// </summary>
public sealed class StartupException(string message) : Exception(message);
