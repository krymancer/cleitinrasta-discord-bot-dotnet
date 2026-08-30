# Vendored YouTube plugin (PR 229)

`youtube-plugin-pr229-42cc5a2.jar` is the Lavalink plugin built from
[lavalink-devs/youtube-source#229](https://github.com/lavalink-devs/youtube-source/pull/229)
(`ashton045/youtube-source` `feat/sabr-support` at
`42cc5a2042f31dba3fdc523618ef380a1a8bb79d`).

That unreleased plugin replaces the static visitor-bound `pot.token` in 1.18.2
with `remotePot`, which mints a **per-video / GVS PoToken** from
[webpo-generator](https://github.com/ashton045/webpo-generator).

## How this JAR was built

```bash
git clone https://github.com/ashton045/youtube-source.git
cd youtube-source
git checkout 42cc5a2042f31dba3fdc523618ef380a1a8bb79d
# Skip RemoteMWebPlaybackTest — it expects a live webpo-generator on :8080
# and is what makes a default JitPack `./gradlew build` fail.
./gradlew :plugin:build -x test --no-daemon
```

Output: `plugin/build/libs/youtube-plugin-42cc5a2042f31dba3fdc523618ef380a1a8bb79d.jar`

SHA-256:

```
5956638abc420d9efc32b11c70168da5dda7a71d2df02c7f688e9770e7738bc7
```

Rebuild with `scripts/build-youtube-plugin.sh`.
