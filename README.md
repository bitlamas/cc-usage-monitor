# cc-usage-monitor

A lightweight **system-tray app** that shows your **Claude Code** usage limits as
**filled progress discs** — so the 5-hour session limit and the weekly limits stay
visible at a glance while you work in the terminal.

Each limit is its own tray icon: a disc that fills as you approach the limit
(green → yellow at 70% → red at 90%). Hover for the exact percentage and reset
time; left-click for a detail popup; right-click to choose which limits to show.

> **Status:** v1, working on **Windows 11/10**. The stack keeps macOS and Linux as
> build targets for a later phase.

## What it does

- **Two default tray discs:** 5-hour session and Weekly (all models).
- **Selectable limits** (right-click): 5-hour, Weekly (all), Weekly (Sonnet). Weekly
  (Opus) appears, disabled, until Anthropic exposes it.
- **Band colors:** green `< 70%`, yellow `70–89%`, red `>= 90%`.
- **Hover tooltip:** `5-hour · 42% · Resets in 2h 13m`. Weekly limits that reset more
  than a day out show the day, e.g. `Weekly · 46% · Resets Sun 10AM`.
- **Left-click → detail popup** listing every selected limit with a usage bar,
  percentage, and reset time. Opens next to the tray; dismisses on click-away.
- **Right-click menu:** toggle limits, alerts, start-at-login, refresh now, open the
  config file, quit.
- **Auto-start at login** and a per-user `config.json` for tuning.

*Deferred to a later release: the visible toast notification (the alert logic is
built and tested), and macOS/Linux.*

## How it works — and your credentials

The app reads **your own** Claude Code login — the credentials Claude Code already
stores on your machine at `~/.claude/.credentials.json`. There is **nothing to
configure and nothing shared**: each person who runs the app uses their own local
login and sees their own usage. If you are not logged in to Claude Code, the app
says so.

It fetches usage from the same OAuth endpoint the Claude Code CLI uses, and when the
token expires it asks the `claude` CLI to refresh its own token — the app never
handles OAuth client secrets, and **never stores or transmits your credentials
anywhere**. No telemetry, no analytics, no third-party calls.

## Build & run

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build
dotnet run --project src/CcUsageMonitor
```

A self-contained, single-file Windows binary:

```bash
dotnet publish src/CcUsageMonitor -c Release -r win-x64 --self-contained
```

## Tech

C# / **.NET 8 (LTS)** · **[Avalonia UI](https://avaloniaui.net/)** `11.2` for the
cross-platform tray · **[SkiaSharp](https://github.com/mono/SkiaSharp)** `2.88` for
the disc rendering.

## License

[GNU GPLv3](LICENSE).
