# Smalland Dedicated Server

[![WindowsGSH](.github/assets/windowsgsh-badge.svg)](https://windowsgsh.com)
[![Status](https://img.shields.io/badge/status-needs_work-f59e0b)](#status)
[![Module version](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.Smalland%2Fmain%2FSmalland.mod%2Fmodule.json&query=%24.version&prefix=v&label=module&color=1E8449)](Smalland.mod/module.json)
[![Requires WindowsGSH](https://img.shields.io/badge/dynamic/json?url=https%3A%2F%2Fraw.githubusercontent.com%2FWindowsGSH%2FWindowsGSH.Smalland%2Fmain%2FSmalland.mod%2Fmodule.json%3Fbadge%3Dminimum&query=%24.minimumWindowsGshVersion&prefix=v&label=requires%20WindowsGSH&color=2563EB)](Smalland.mod/module.json)
[![Licence](https://img.shields.io/badge/licence-MIT-64748B)](LICENSE.md)

This WindowsGSH module installs, configures, launches, imports and backs up Smalland: Survive the Wilds dedicated servers.

## Status

**NEEDS WORK.** Installation, executable/config generation and import are implemented, but current public-port requirements, discovery, shutdown and log behavior need live validation.

## Installation

WindowsGSH installs SteamCMD app `808040` anonymously and launches `SMALLAND/Binaries/Win64/SMALLANDServer-Win64-Shipping.exe`. Online multiplayer requires Epic Online Services; the module preserves the EOS values supplied by the installed `start-server.bat` without documenting or logging their secret values.

### Import an existing server

Import accepts a direct install or a WindowsGSM parent containing `serverfiles`. It previews supported `smalland_conf.json` and shipped start-script values, then offers Copy or Adopt without changing the source during preview.

## Configuration

The module writes preservation-aware `smalland_conf.json` values for password, privacy and gameplay modifiers. Server/world identity and the game port are passed on the launch command. Unknown JSON and installed EOS values are preserved.

## Networking

| Purpose | Default | Protocol | Exposure |
|---|---:|---|---|
| Game/direct connection | 7777 | UDP | Public for remote players; eligible for forwarding/UPnP |
| Browser/query candidate | game port + 1 | UDP | Unverified and private/no automatic forwarding until socket-tested |

Current community guidance conflicts over whether only UDP `7777` or both `7777` and `7778` are needed. The adjacent port is therefore derived rather than independently configurable, but is deliberately not forwarded automatically until a current server proves it is required.

## Query, console, and administration

WindowsGSH reports process status only: no A2S/player query or RCON capability is claimed. The module uses log-tail-only output from `SMALLAND/Saved/Logs`; it does not present stdin command input because no supported console-command channel has been established.

## Files and backups

The server executable is beneath `SMALLAND/Binaries/Win64`; saves, Windows server config and logs are beneath `SMALLAND/Saved`. Backups include saves, Windows config and `smalland_conf.json`. Logs are currently included as an optional target and can be omitted when archive size matters.

## Known limitations

- Remote discovery and the adjacent query/browser port are unverified.
- No player-count, RCON or console-input provider is implemented.
- Close-window shutdown with forced fallback requires save-safety testing.
- Epic Online Services prerequisites must be verified after a clean install/update.

## Beta verification checklist

- [ ] Fresh-install/update app `808040` and verify executable and EOS startup prerequisites.
- [ ] Round-trip config values while preserving unknown JSON/EOS content.
- [ ] Start, attach/reattach the PID, tail logs and prove a save-safe stop/session ending.
- [ ] Capture listening sockets; test direct join, browser discovery and cross-play with only required UDP ports forwarded.
- [ ] Test direct/WindowsGSM Copy and Adopt import, crash handling, backup and restore.

## Support

Report issues through the [WindowsGSH.Smalland tracker](https://github.com/WindowsGSH/WindowsGSH.Smalland/issues) with versions, sanitized config/logs and the operation performed. Never post passwords, EOS values, player identifiers or unredacted archives.

## Support development

If you like the work I do and would like to support continued WindowsGSH and module development, you can contribute here:

- [Ko-fi](https://ko-fi.com/shenniko)
- [PayPal](https://paypal.me/shenniko)

## Trust and source

Modules execute with WindowsGSH's Windows permissions. Download from a trusted source and review `Smalland.mod/module.json` and `SmallandModule.cs`. See [SECURITY.md](SECURITY.md). Game files remain subject to their publisher's terms.
