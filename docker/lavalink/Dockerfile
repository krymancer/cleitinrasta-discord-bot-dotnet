# Official Lavalink with the PR 229 youtube-plugin baked in.
# Used by CI as an optional image; cluster manifests prefer the official
# image plus an initContainer that copies the vendored JAR so a new GHCR
# package is not required for rollback to ghcr.io/lavalink-devs/lavalink:4.
FROM ghcr.io/lavalink-devs/lavalink:4

COPY third_party/youtube-plugin-pr229-42cc5a2.jar /opt/Lavalink/plugins/youtube-plugin.jar
