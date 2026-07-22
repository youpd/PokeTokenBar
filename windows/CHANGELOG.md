# Changelog

All notable changes to the Windows port are documented in this file.

## Unreleased

### Changed

- Show Codex model usage as an API-equivalent cost estimate and label it as subscription-plan usage,
  instead of forcing the displayed cost to zero.
- Add explicit current API rates for GPT-5.6 Sol, Terra, Luna, GPT-5.4, and GPT-5.3-Codex.

### Fixed

- Render and cache PokéAPI's pixel-art egg sprite in the flyout and notification area, with the
  emoji retained only as an offline fallback.
- Scope OpenAI status warnings to Codex API, web, desktop, CLI, and VS Code components instead of
  showing unrelated ChatGPT incidents as Codex degradation.
- Reuse pre-rendered notification-area icon frames and release native icon handles, preventing the
  GDI+ failure that could terminate the app during long-running tray animation.
- Recover narrowly from any remaining `H.NotifyIcon` GDI rendering exception instead of closing the
  whole application.

### Documentation

- Clarify runtime sprite caching, GitHub issue reporting, local log privacy, and launch-at-login
  troubleshooting.

## 0.1.0 - 2026-07-22

### Added

- Initial self-contained Windows 10/11 tray application.
- Local Claude Code, Codex, and Gemini CLI usage tracking with cost, limits, alerts, and provider
  status.
- Companion egg, evolution, shop, bag, collection, multilingual UI, updates, diagnostics, and
  release packaging.
