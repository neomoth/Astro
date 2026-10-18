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
- ~~Add packaging~~ DONE

## Setup
### Running the bot from source
Clone the git repository, then `cd` into it.
After that, run `dotnet run --project Astro.Bot`, and voila, the bot has failed to run.
In order to make the bot connect, you need to supply your bot's token in the newly generated `/Resources/Config/config.json`.

Once you've pasted the bot token and configured the bot to your liking, execute `dotnet run --project Astro.Bot`
again and NOW the bot should connect.

When run from the repo (any build configuration), the bot keeps all of its files in `Resources/`.

### Building a release
```
dotnet build Astro.Packaging -t:Package
```
This builds a self-contained executable for Linux (x64, arm64), Windows (x64) and macOS (x64, arm64), and packages each
into `artifacts/` as `astro-<version>-<platform>.tar.gz` (`.zip` for Windows). For specific platforms only, add e.g.
`-p:PackageRuntimes=linux-x64`; separate several with `%3B` (an escaped `;`, which MSBuild would otherwise read as a
separator), e.g. `-p:PackageRuntimes=linux-x64%3Blinux-arm64`. In Rider, the **Package (Linux)** run configuration does
this for both Linux targets. The version is set in `Directory.Build.props`.

### Running a release
Download the archive for your platform from the releases, extract it, and run `astro` (`astro.exe` on Windows). It's a
single self-contained executable, so .NET doesn't need to be installed.

The first run creates a config file and exits, asking for your bot's token. Paste the token into `config.json`, then
run it again.

Files are kept in your platform's usual folders:

|         | Config (`config.json`, secret key)           | Data (database)                            | Logs                        |
|---------|----------------------------------------------|--------------------------------------------|-----------------------------|
| Linux   | `~/.config/astro`                            | `~/.local/share/astro`                     | `~/.local/state/astro/logs` |
| Windows | `%APPDATA%\Astro`                            | `%LOCALAPPDATA%\Astro`                     | `%LOCALAPPDATA%\Astro\Logs` |
| macOS   | `~/Library/Application Support/Astro/Config` | `~/Library/Application Support/Astro/Data` | `~/Library/Logs/Astro`      |

On Linux, `XDG_CONFIG_HOME`, `XDG_DATA_HOME` and `XDG_STATE_HOME` are respected. To keep everything in one folder
instead (portable installs, containers), run `astro --home <dir>` or set `ASTRO_HOME`. `astro --paths` prints where
files are kept.

Back up the secret key (`keys/secrets.key` under the config folder) along with your data: OAuth tokens in the database
are encrypted with it.

### Publishing a release
Run `./release.sh <version>` (e.g. `./release.sh 0.2.0`, or `0.3.0-beta.1` for a pre-release) from a clean `main`.
It updates the version in `Directory.Build.props`, commits it, and pushes a `v<version>` tag. That triggers the
**Release** GitHub workflow, which builds every platform on its own OS and attaches the archives to a GitHub release.
`--retag` moves an existing tag to the current commit and replaces that release's files.
