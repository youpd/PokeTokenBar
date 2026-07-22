# AGENTS.md — Windows port working rules

Everything under `windows/` is the Windows port of PokeTokenBar. **`windows/PLAN.md` is the source of
truth** — read it before writing any code. When the plan is ambiguous, consult the original Swift
implementation under `../Sources/PokeTokenBar/` (file mapping in PLAN §15) and match its behavior.

## Fixed decisions (do not relitigate)

- Stack: **.NET 10 (LTS) + WPF**. Tray: `H.NotifyIcon.Wpf`. Toasts: `Microsoft.Toolkit.Uwp.Notifications`.
  Tests: xUnit. No other NuGet packages without explicit user approval — BCL first
  (`System.Text.Json`, `ZLibStream`, `HttpClient`).
- Layout: `windows/PokeTokenBar.sln`, `windows/src/PokeTokenBar.Core` (net10.0, **no UI references**),
  `windows/src/PokeTokenBar.App` (net10.0-windows10.0.17763.0), `windows/tests/PokeTokenBar.Tests`.
- Provider ids stay `"claude_code"`, `"codex"`, `"gemini"` (parity with macOS state/snapshot files).
- `companion-state.json` keeps the exact macOS schema/keys (states must be copyable across platforms).
- Never bundle Pokémon assets (license) — runtime fetch from PokéAPI + local cache only.

## Extension contract (violations are review defects — PLAN §5)

- New usage source = one `IUsageProvider` implementation + one registry entry. Nothing else.
- Generic aggregation (today/week/month totals, burn tier, companion rhythm) must be provider-agnostic
  (reduce over all snapshots). No `== "claude_code"`-style literals in generic paths.
- Provider-specific branches by `providerId` only for provider-unique features (official limits,
  5h forecast, "current block" row).
- Binary search paths only in `BinaryLocator.CommonToolDirectories()`.

## Invariants (PLAN §11, I1–I13) — review checklist

Highlights: semantic checks instead of always-true null checks (I1) · tray limit line gated by
"tokens today > 0", never by `limits != null` (I3) · alerts/candy are edge-triggered with stable keys,
no volatile fields like `resets_at` in keys, pure evaluation functions (I4) · full toggle-combination
table test for tray text (I5) · no time-based dimming of the tray icon (I6) · background polling must
never trigger any interactive UI, incl. console window flashes — `CreateNoWindow=true` everywhere (I7)
· keep-previous on failures (I8) · midnight date guards (I9) · single process-spawn choke point (I10).

## Workflow

- Work milestone by milestone (PLAN §13, M0→M6). One milestone = one PR. Do not start the next
  milestone in the same PR.
- DoD per milestone = its listed DoD **plus** `dotnet test windows/PokeTokenBar.sln` green. Include the
  test run output when reporting completion. Never claim completion without it.
- Before M1: verify the assumptions in PLAN §14 against this machine's real logs and report findings.
- Commits/PRs in English. Do not touch anything outside `windows/` except `.github/workflows/windows-ci.yml`
  (and only that workflow).

## Commands

```
dotnet build windows/PokeTokenBar.sln
dotnet test  windows/PokeTokenBar.sln
dotnet publish windows/src/PokeTokenBar.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```
