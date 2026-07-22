# AGENTS.md — repository guide for coding agents

This repository contains **two apps**:

1. **macOS menu bar app (Swift)** at the repo root — `Package.swift`, `Sources/`, `Tests/`, `scripts/`.
   This is the original, shipping app.
2. **Windows port (C# / .NET + WPF)** under `windows/` — in progress.

## Ground rules

- **Working on the Windows port?** Read `windows/AGENTS.md` and follow the spec in `windows/PLAN.md`.
  All Windows work lives under `windows/` only.
- **Do NOT modify the macOS area** (`Package.swift`, `Sources/`, `Tests/`, `scripts/`, `assets/`,
  root READMEs, `.github/workflows/ci.yml`) unless the user explicitly asks. The Swift sources are the
  behavioral reference for the port — read them freely, change them never.
- **Language**: PR titles/bodies and commit messages are **English only** (repo convention), even when
  instructed in Korean.
- **Release tags**: macOS uses `vX.Y.Z`; Windows uses `win-vX.Y.Z`. Never create or move a tag of the
  other platform.
