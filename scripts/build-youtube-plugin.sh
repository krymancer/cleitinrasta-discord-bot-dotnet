#!/usr/bin/env bash
# Rebuild third_party/youtube-plugin-pr229-42cc5a2.jar from youtube-source PR 229.
set -euo pipefail

SHA="${YOUTUBE_SOURCE_SHA:-42cc5a2042f31dba3fdc523618ef380a1a8bb79d}"
DEST="$(cd "$(dirname "$0")/.." && pwd)/third_party/youtube-plugin-pr229-42cc5a2.jar"
WORKDIR="$(mktemp -d)"
trap 'rm -rf "$WORKDIR"' EXIT

git clone --depth 1 --branch feat/sabr-support https://github.com/ashton045/youtube-source.git "$WORKDIR"
git -C "$WORKDIR" fetch --depth 1 origin "$SHA"
git -C "$WORKDIR" checkout "$SHA"

# RemoteMWebPlaybackTest needs a live token service; skip tests (same as a
# successful local Gradle build of this branch).
(cd "$WORKDIR" && ./gradlew :plugin:build -x test --no-daemon)

cp "$WORKDIR/plugin/build/libs/youtube-plugin-${SHA}.jar" "$DEST"
echo "Wrote $DEST"
sha256sum "$DEST"
