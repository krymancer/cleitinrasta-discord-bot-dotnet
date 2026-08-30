# YouTube playback: remote PoToken (PR 229)

Argo CD application **`discord-bot`** syncs repository path **`k8s`** from **`main`**.
Apply that whole directory together — do not sync a subset of these files.

## Why 1.18.2 still 403s

YouTube now requires a **per-video / GVS PoToken**. Plugin **1.18.2** can only
attach one static visitor-bound token (`lavalink-pot` + CronJob). Finding a
track and emitting `TrackStartEvent` is not enough; the googlevideo request
is rejected (`AllClientsFailedException`, MWEB 403, ANDROID “requires login”).

A manual `potoken-renew` on 2026-08-29 produced a new token hash and Lavalink
loaded it. Playback still 403’d. This is not an expired token or a dead CronJob.

## What this change does

1. Loads the unreleased [youtube-source PR 229](https://github.com/lavalink-devs/youtube-source/pull/229)
   plugin JAR (`third_party/youtube-plugin-pr229-42cc5a2.jar`, built from
   `42cc5a2042f31dba3fdc523618ef380a1a8bb79d` with `./gradlew :plugin:build -x test`)
   into official `ghcr.io/lavalink-devs/lavalink:4` via an initContainer.
2. Runs [webpo-generator](https://github.com/ashton045/webpo-generator)
   (`ca301fd`) so Lavalink can `POST /generate` a token bound to each video id.
3. Drops the static `pot.token` / `potoken-renew` CronJob and `bgutil-pot-provider`.

`cipher.kikkia.dev` (signature decipher) and the OAuth refresh-token secret
are unchanged.

Local smoke test (Lavalink 4.2.2 + this JAR + webpo-generator):
`GET /youtube/stream/dQw4w9WgXcQ` returned **200** `audio/webm` (3.3 MiB).
Logs showed `Generating remote pot for content binding: dQw4w9WgXcQ`.

## Deploy via Argo

1. Merge to `main` so `third_party/youtube-plugin-pr229-42cc5a2.jar` is
   fetchable from GitHub raw (the Lavalink initContainer URL points at `main`).
2. Sync Argo app **`discord-bot`** (path `k8s`, dest `main`). Enable **prune**
   so `bgutil-pot-provider` / `potoken-renew` / `potoken-renewer` go away.
3. `webpo-generator` uses public `node:24-bookworm-slim` and installs
   webpo-generator at first start (allow ~1–2 minutes for `npm ci`).
   Lavalink waits on `/health`.
4. Confirm Lavalink logs:
   - `Using remote poToken service with url "http://webpo-generator..."`
   - `Using remote cipher server with url "https://cipher.kikkia.dev"`
   - `Generating remote pot for content binding: <videoId>`
5. Play a YouTube track end-to-end (not just search).

If `raw.githubusercontent.com` is blocked from the cluster, switch the Lavalink
image to the CI-built `ghcr.io/krymancer/cleitinrasta-lavalink:yt-pr229` (plugin
baked in) and drop the `fetch-youtube-plugin` initContainer. Make that GHCR
package public, same as the bot image.

CI also publishes `ghcr.io/krymancer/cleitinrasta-webpo-generator:yt-pr229`
from `docker/webpo-generator/Dockerfile` for a faster webpo start later.

## One-step rollback to plugin 1.18.2

After this change is on `main`:

```bash
git revert --no-edit <merge-commit-sha>
git push origin main
```

Then sync Argo app **`discord-bot`**. That restores official Lavalink + Maven
plugin **1.18.2**, static `pot.token` / `visitorData`, `bgutil-pot-provider`,
and the 4-hour `potoken-renew` CronJob.

If you only want to rewind manifests (leave docs/CI alone):

```bash
git checkout 4ee3472 -- k8s
rm -f k8s/webpo-generator.yaml
git add k8s
git commit -m "rollback: YouTube plugin 1.18.2 + static poToken"
git push origin main
```

Snapshots of the 1.18.2 manifests live in `docs/rollback-1.18.2/`.
