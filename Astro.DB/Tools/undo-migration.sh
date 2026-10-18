#!/bin/bash
# Removes the most recent migration (e.g. you made a typo), reverting it from the local database if it was applied.
set -euo pipefail

cd "$(dirname "$0")/.."
export PATH="$PATH:$HOME/.dotnet/tools"

dotnet ef migrations remove --force 2>&1 | grep -v "tools version"
