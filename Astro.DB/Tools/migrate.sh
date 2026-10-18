#!/bin/bash
# Usage: Tools/migrate.sh [MigrationName]
# Run after changing anything in Astro.DB/Entities. Detects what changed, generates a migration for it,
# shows a summary, and applies it to the local database. The name is optional (it's just a label).
set -euo pipefail

cd "$(dirname "$0")/.."
export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_NOLOGO=1

ef() { dotnet ef "$@" 2>&1 | grep -v "tools version" || true; }

echo "Building and checking for model changes..."
build_output=$(dotnet build --nologo -v q -clp:ErrorsOnly 2>&1) || { echo "$build_output"; exit 1; }
if dotnet ef migrations has-pending-model-changes --no-build >/dev/null 2>&1; then
	echo "No changes to the entities since the last migration. Nothing to do."
	exit 0
fi

name="${1:-Auto$(date +%Y%m%d%H%M%S)}"
ef migrations add "$name" --no-build >/dev/null

file=""
for f in Migrations/*_"$name".cs; do
	[[ -f "$f" && "$f" != *.Designer.cs ]] && file="$f"
done
[[ -n "$file" ]] || { echo "Could not find the generated migration for $name."; exit 1; }
echo
echo "Created $file:"
# Summarise only the Up() method (what gets applied), one line per operation.
sed -n '/void Up(/,/void Down(/p' "$file" | awk '
	/migrationBuilder\./ { match($0, /migrationBuilder\.[A-Za-z]+/); op = substr($0, RSTART + 17, RLENGTH - 17); name = ""; table = ""; newName = "" }
	/^ *name:/     { if (name == "") { name = $2; gsub(/[",]/, "", name) } }
	/^ *table:/    { table = $2; gsub(/[",);]/, "", table) }
	/^ *newName:/  { newName = $2; gsub(/[",)]/, "", newName) }
	/\);$/ && op != "" {
		line = "  " op " " (table != "" ? table "." : "") name (newName != "" ? " -> " newName : "")
		if (op ~ /^(Drop|Rename)/) line = line "   <-- existing data in this is deleted/moved!"
		print line; op = ""
	}'

if grep -qE 'migrationBuilder\.(Drop|Rename)' <(sed -n '/void Up(/,/void Down(/p' "$file"); then
	echo
	read -rp "This migration deletes or renames existing data. Apply it? [y/N] " answer
	if [[ ! "$answer" =~ ^[Yy]$ ]]; then
		echo "Not applied. Tools/undo-migration.sh removes it; or leave it and the bot applies it on next start."
		exit 0
	fi
fi

echo
ef database update | grep -E "Applying|Done|rror"
