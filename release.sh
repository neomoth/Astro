#!/usr/bin/env bash
# Sets the version, pushes main and tags a release, which triggers the GitHub release workflow
# (.github/workflows/release.yml). That builds Astro for every platform and attaches the archives to a GitHub release.
#
# Usage: ./release.sh <version> [--retag]
#   ./release.sh 0.2.0
#   ./release.sh 0.3.0-beta.1     # published as a pre-release
#   ./release.sh 0.2.0 --retag    # move an existing tag to HEAD and rebuild that release's files
set -euo pipefail

version="${1:-}"
retag="${2:-}"

if [[ -z "$version" ]]; then
	echo "usage: $0 <version> [--retag]" >&2
	exit 1
fi

version="${version#v}"
tag="v$version"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
	echo "error: '$version' isn't a version like 1.2.3 or 1.2.3-beta.1" >&2
	exit 1
fi

cd "$(dirname "$0")"

if [[ -n "$(git status --porcelain)" ]]; then
	echo "error: working tree has uncommitted changes; commit them first" >&2
	exit 1
fi

if [[ "$(git branch --show-current)" != "main" ]]; then
	echo "error: not on main" >&2
	exit 1
fi

if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
	if [[ "$retag" != "--retag" ]]; then
		echo "error: tag $tag already exists; bump the version, or pass --retag to rebuild it" >&2
		exit 1
	fi
	git tag -d "$tag"
	git push origin ":refs/tags/$tag" || true
fi

# Keep Directory.Build.props in step, so local builds report the released version too.
props="Directory.Build.props"
sed -i.bak -E "s|<Version>[^<]*</Version>|<Version>$version</Version>|" "$props" && rm -f "$props.bak"
if [[ -n "$(git status --porcelain "$props")" ]]; then
	git add "$props"
	git commit -m "Release $tag"
fi

git push origin main
git tag "$tag"
git push origin "$tag"

echo "Pushed $tag. Watch the run under the repo's Actions tab; the release appears once every platform has built."
