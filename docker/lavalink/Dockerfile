# Official Lavalink with the PR 229 youtube-plugin baked in.
# Used by docker-compose and by CI as an optional image; cluster manifests prefer the
# official image plus an initContainer that copies the vendored JAR so a new GHCR
# package is not required for rollback to ghcr.io/lavalink-devs/lavalink:4.
FROM ghcr.io/lavalink-devs/lavalink:4

# The plugins folder must be writable by the lavalink user: plugins listed under
# lavalink.plugins in application.yml (LavaSrc) are downloaded into it at startup.
USER root
RUN mkdir -p /opt/Lavalink/plugins && chown lavalink:lavalink /opt/Lavalink/plugins
USER lavalink

COPY --chown=lavalink:lavalink third_party/youtube-plugin-pr229-1ddb648.jar /opt/Lavalink/plugins/youtube-plugin.jar
