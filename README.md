# Astro
My first real attempt at writing a Discord bot in C#.

## TODO
- Fill out the abstractions library a bit more
- Flesh out behaviors and move to their own manager
- Make a behavior command to manage autoload behaviors and what behaviors are currently active or not
- ~~Set up database stuff for multi-server/user config support~~ DONE
- Add proper moderation commands
- Add more silly commands
- Add more silly systems
- Silly
- Super silly
- Kill God
- Make this README actually look good™

## Setup
### Running the bot as-is
Right now? Not supported. Bot only works when building and running from within a local copy of the repository, because I find it more convenient that way. Once it's a bit more complete, I will package it up all nice and neat so that it works locally as just the executable file.

### Running the bot from source
Clone the git repository, then `cd` into it.
After that, run `dotnet run --project Astro`, and voila, the bot has failed to run.
In order to make the bot connect, you need to supply your bot's token in the newly generated `/Resources/Config/config.json`.

Once you've pasted the bot token and configured the bot to your liking, execute `dotnet run --project Astro` again and NOW the bot should connect.
