# DJ Cleiton Rasta

Este é um bot do discord criado com a finalidade de entender como funciona a criação de bots utilizando a blibioteca Discord.Net.
Esse bot foi criado devido a morte do Groovy, um bot de música excelente que utilizei durante muito tempo, e ainda hoje não achei nenhum bot que substitua ele, então decidi criar o meu próprio bot de música.

Lógico que mesmo este está longe de subistituir o Groovy e mal tem funcionalidade basicas, mas conforme o aprendizado for aumentando, mais funcionalidades serão adicionadas.

## Configuração do bot

Para poder utilizar o bot é necessário editar o arquivo `appsettings.json` na pasta `src/ConsoleApp`, o arquivo deve seguir o seguinte exemplo:

```json
{
  "Discord": {
    "Token": "SEU TOKEN DO BOT DO DISCORD, pegue na aba de bot do seu bot no discord developer portal",
    "Prefix" : "SEU Prefixo, pode ser qualquer coisa, recomendo que evite prefixes de outros bots, eu costumo usar &"
  }
}
```

## Compartilhar tela (`/live`)

O comando `/live` cria uma sala de videochamada (Google Meet ou Jitsi) e manda o link no canal. Veja [docs/live-command.md](docs/live-command.md) para configurar o Google Meet.

## Music commands

Playback is driven by a single **Now Playing** panel. It's posted when a song starts and updates in place (progress bar, requester, up next), with buttons for back, pause/resume, skip, stop, loop, shuffle, autoplay and clear.

| Command | What it does |
| --- | --- |
| `/play <query>` | Play or queue a song/playlist. Accepts searches and YouTube or Spotify links; suggests results while you type. |
| `/playnext <query>` | Put a song or playlist at the front of the queue. |
| `/remove <position>`, `/move <from> <to>` | Edit the queue (positions as shown in `/queue`). |
| `/seek <time>` | Jump to `1:30`, `90` or `1:02:03`. |
| `/volume [0-200]` | Show or set the volume. |
| `/lyrics [song]` | Lyrics of the current song (or a search), from lrclib.net. |
| `/playlist save\|load\|list\|delete` | Saved server playlists. `load` can shuffle. |
| `/pause`, `/resume` | Pause / resume. `/resume` after `/stop` restarts the queue. |
| `/skip` | Skip the current song. |
| `/stop` | Stop the current song but **keep** the queue. |
| `/clear` | Empty the queue and stop playback. |
| `/leave` | Disconnect from the voice channel. |
| `/loop [track\|queue\|off]` | Loop the song or the queue (no argument cycles). |
| `/shuffle` | Toggle shuffle. |
| `/autoplay` | When the queue ends, keep playing similar songs. |
| `/queue` | Show the queue. |
| `/nowplaying` | Re-post the panel at the bottom of the chat. |
| `/history` | Replay recent searches. |
| `/status [check]` | Lavalink version, plugins, YouTube health and recent problems (only you see it). |
| `/live` | Create a video call room and mention everyone in your voice channel. |

Spotify links are handled by the [LavaSrc](https://github.com/topi314/LavaSrc) Lavalink plugin and played from YouTube. Spotify links need Spotify app credentials (`SPOTIFY_CLIENT_ID` / `SPOTIFY_CLIENT_SECRET`, see `.env.example`; in k8s the optional `lavalink-spotify` secret with `clientId` / `clientSecret`).

### Data and restarts

History, saved playlists and a snapshot of each queue live in a SQLite file (`Storage:DatabasePath`, default `/app/data/bot.db`). docker-compose keeps it in the `bot-data` volume; k8s uses the `discord-bot-data` PVC (`k8s/bot-data.yaml`). If the bot restarts while music is playing and people are still in the voice channel, it rejoins and resumes the song where it was (snapshots older than `Storage:RestoreMaxAgeMinutes`, default 30, are ignored).

### Problem alerts

Set `Alerts:ChannelId` (e.g. your `#logs` channel) and/or `Alerts:UserId` (DMs you) to get told when:

- a song fails to load or play (with Lavalink's error),
- a song "finishes" well before its length (how the YouTube SABR breakage showed up, see `docs/youtube-playback.md`),
- a track gets stuck,
- the connection to Lavalink drops / comes back,
- the background YouTube check (every `Alerts:HealthCheckMinutes`, default 30) starts failing / recovers.

The same problem is reported at most once every `Alerts:CooldownMinutes` (default 30). In docker-compose use `ALERTS_CHANNEL_ID` / `ALERTS_USER_ID`; in k8s add those keys to `discord-bot-secret`.
