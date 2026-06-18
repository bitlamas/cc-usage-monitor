# cc-usage-monitor

A lightweight, cross-platform **system-tray / menu-bar app** that shows your
**Claude Code Max** usage limits as **circular progress icons** — so the
5-hour session limit and weekly limits stay visible at a glance while you work
in the terminal.

Each limit is its own tray icon: a colored ring that fills as you approach the
limit (green → yellow at 70% → red at 90%), with the exact percentage, reset
countdown, and last-updated time on hover.

> **Status:** in design. Windows 11 is the v1 target; the chosen stack keeps
> macOS and Linux as build targets (validated in a later phase), not a rewrite.

## Why

When you live in the terminal, the 5-hour session limit and the weekly limits
are invisible until you hit them. This app keeps them in the corner of your eye:
a glanceable ring per limit, no window to open, no tab to check.

## Features (planned v1)

- Two default tray icons: **5-hour session** and **Weekly (all models)**.
- Selectable limits: 5-hour session, Weekly (all), Weekly (Sonnet), Weekly (Opus).
- Color thresholds: green `< 70%`, yellow `70–89%`, red `>= 90%`.
- Optional percentage rendered inside the ring (off by default).
- Hover tooltip: exact %, reset countdown, last-updated time.
- Left-click → detail flyout listing all selected limits.
- Right-click → context menu (toggle limits, alerts, start-at-login, refresh).
- One toast alert per limit at `>= 90%`, re-armed after that limit resets.
- Auto-start at login. Single self-contained binary, no runtime install.

## How it works

Usage data comes from the same OAuth endpoint the Claude Code CLI uses. The app
reads your **local** Claude Code credentials and **never stores or transmits
them elsewhere**. When the token expires, it asks the `claude` CLI to refresh
its own token — the app never handles OAuth client secrets.

## Tech

C# / **.NET 8 (LTS)** · **[Avalonia UI](https://avaloniaui.net/)** for the
cross-platform `TrayIcon` · **[SkiaSharp](https://github.com/mono/SkiaSharp)**
to render the rings · single self-contained, single-file binary per OS.

## Status & roadmap

This is an active, spec-driven rebuild — **not yet released**.

- [ ] v1 design spec (refined from the validated reference design)
- [ ] Implementation plan
- [ ] Core (credentials, usage client, poller, ring renderer, alerts, config)
- [ ] Tray UI (Avalonia) — Windows 11
- [ ] Packaging — `win-x64` self-contained binary
- [ ] macOS / Linux validation

Build and install instructions will land here once v1 is buildable.

## Privacy

Credentials never leave your machine. The app talks only to Anthropic's usage
endpoint, using the token Claude Code already stores locally. No telemetry, no
analytics, no third-party calls.

## License

[GNU GPLv3](LICENSE).
