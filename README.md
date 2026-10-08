# Aerocord

A third-party Discord client for Windows, built with .NET Framework 4.8 / WinForms,
skinned to look and feel like classic MSN Messenger — buddy list with groups,
tiled contacts with status "lights," pop-up conversation windows, nudges, and a
system tray icon so it can run quietly in the background.

## ⚠️ Please read before using

This client signs in with your **personal Discord account token**, not a bot
token. That's what makes "your real friends list, DMs, and servers, skinned as
MSN" possible — but third-party clients that automate a normal user account
("self-bots") are against **Discord's Terms of Service**, and Discord does
actively detect and act on this. Possible consequences range from nothing to
a warning, temporary lock, or permanent ban. This is your call to make, but
please make it deliberately:

- Consider using an alt/throwaway account, not your main.
- Never share your token with anyone or paste it anywhere public — it's
  equivalent to your password.
- This project is provided as-is, for personal educational use.
- Also note that this release is not stable at all for daily use, use it only for fun.

## What actually works

- Sign in with a token (optionally remembered locally, encrypted with Windows
  DPAPI so it's tied to your Windows login).
- Friends list with live presence (online/away/busy/offline), MSN-style tiles.
- Servers & text channels, browsable in a collapsible MSN-style group list.
- Group DMs.
- Real-time messaging: send/receive, with a scrollback of the last 50 messages
  loaded per conversation.
- Typing indicators ("X is typing...").
- Status switching (Online/Away/Busy/Appear Offline) with a custom status
  message, reflected on Discord for real.
- Desktop notifications (tray balloon + sound) for new messages when a
  conversation window isn't focused.
- A "Nudge" button — shakes the chat window and pings the other person with a
  message so they know (this is a fun local recreation, not a real Discord
  feature, so it only "does something" visually for people also using this
  client, but it does send a real chat message either way).
- Minimize-to-tray, close-to-tray, and a tray context menu (status switcher,
  open, exit) so it behaves like a real background messenger app.

## What's out of scope / limited

- **Voice/video calls**: joining a voice channel is implemented at the
  *signaling* level only (the voice gateway handshake, UDP IP discovery,
  session negotiation) — Discord will show you as connected to the channel.
  Actual audio capture/encode (Opus) and playback are **not** implemented,
  because that needs native/NuGet packages (e.g. Concentus for managed Opus,
  NAudio for mic/speaker access) that couldn't be fetched in the offline
  environment this was built in. The code has clear `TODO(audio)` markers
  showing exactly where to wire that in — see `Core/DiscordVoiceClient.cs`.
- No screen share, threads, forums, stickers, embeds rendering, reactions,
  file upload/download, message editing, or emoji/GIF picker yet. The
  architecture (REST + Gateway clients, `AppState`, message model) is set up
  so these are all incremental additions, not rewrites.
- No message search, read receipts, or push notifications when fully closed
  (it needs to be running, even if minimized to tray, to receive anything).

## Building it

You'll need **Visual Studio 2019 or 2022** (Community edition is fine) on
Windows with the **.NET desktop development** workload installed.

1. Open `Aerocord.sln`.
2. Build (Ctrl+Shift+B). The project references only built-in .NET Framework
   assemblies — **no NuGet packages required**, so it should build offline.
3. Run (F5). Paste your token on the sign-in screen (see in-app "How do I get
   my token?" link for instructions) and sign in.

## Project layout

```
Aerocord/
  Program.cs                 entry point
  Core/
    Json.cs                  dependency-free JSON parser/writer
    Models.cs                DiscordUser/Guild/Channel/Message/Relationship
    DiscordRestClient.cs     HTTP API (guilds, channels, messages, DMs, friends)
    DiscordGatewayClient.cs  realtime WebSocket gateway (identify, heartbeat, events)
    DiscordVoiceClient.cs    experimental voice-channel signaling (no audio yet)
    AppState.cs              shared session state + caches, consumed by the UI
    TokenStorage.cs          DPAPI-encrypted "remember me" token storage
  UI/
    MsnTheme.cs               shared colors/fonts/gradient helpers
    LoginForm.cs               MSN-style sign-in window
    BuddyListControls.cs       custom-drawn group headers & contact tiles
    MainForm.cs                buddy list window + system tray
    ChatWindowForm.cs          conversation window (transcript, input, nudge)
    IconFactory.cs             loads Assets\*.ico at runtime (falls back to a
                                generated placeholder if missing)
  Assets/
    app.ico                    exe/taskbar/window icon - replace with your own
    tray.ico                   system tray icon - replace with your own
```



## Future features

- **Real voice audio**: adding NuGet packages `Concentus` (managed Opus) and
  `NAudio` (mic capture / speaker playback), then fill in the `TODO(audio)`
  spots in `DiscordVoiceClient.cs` to encode mic frames into RTP packets
  (encrypted with the `_secretKey` using xsalsa20poly1305 — `Sodium.Core` or a
  managed NaCl implementation) and decode incoming ones.
- **Reactions/embeds/attachments**: extend `DiscordMessage`/`Models.cs` and
  the `MESSAGE_CREATE` handling in `DiscordGatewayClient.cs`, then render them
  in `ChatWindowForm.AppendMessage`.
- **Avatars**: `DiscordUser.AvatarHash` is already parsed; fetch
  `https://cdn.discordapp.com/avatars/{id}/{hash}.png` with `HttpClient` and
  draw it in place of the initials circle in `ContactTileControl`.
