# Vendored YouTube plugin (PR 229)

`youtube-plugin-pr229-1ddb648.jar` is the Lavalink plugin built from
[lavalink-devs/youtube-source#229](https://github.com/lavalink-devs/youtube-source/pull/229)
(`ashton045/youtube-source` `feat/sabr-support` at
`1ddb648257d5b547aa1e2dfb8104edf6cb1089be`).

That unreleased plugin replaces the static visitor-bound `pot.token` in 1.18.2
with `remotePot`, which mints a **per-video / GVS PoToken** from
[webpo-generator](https://github.com/ashton045/webpo-generator).

## How this JAR was built

```bash
git clone https://github.com/ashton045/youtube-source.git
cd youtube-source
git checkout 1ddb648257d5b547aa1e2dfb8104edf6cb1089be
# Skip RemoteMWebPlaybackTest — it expects a live webpo-generator on :8080
# and is what makes a default JitPack `./gradlew build` fail.
./gradlew :plugin:build -x test --no-daemon
```

Output: `plugin/build/libs/youtube-plugin-1ddb648257d5b547aa1e2dfb8104edf6cb1089be.jar`

SHA-256:

```
1ea08818f77c9ed4a9a9d272f3fa7b4e762055fdc269b0faff29c434c1f9c95d
```

Rebuild with `scripts/build-youtube-plugin.sh`.
