# NIRA Runtime V2 — Phase 0 Bootstrap

A clean, independent .NET 10 foundation for **one NIRA identity**, a durable, bounded agentic workflow loop, authorization gates, checkpointed progress, and structured research documents.

**Important:** This is the FIRST engineering milestone, not the finished AI agent. Live GPT-OSS/Ultra, official image retrieval, memory migration, WPF integration, and production encrypted persistence remain future work. Existing NIRA stays operational.

## Installation (Windows 11, .NET SDK 10)

1. Extract the supplied ZIP to a temporary directory. Keep its `NIRA-V2` folder intact.
2. From PowerShell in the extracted folder, run:

```powershell
& .\NIRA-V2\scripts\Verify-Foundation.ps1 -IncludeDemo
```

3. To place the clean V2 folder beside your existing NIRA projects **without overwriting or deleting anything**:

```powershell
& .\NIRA-V2\scripts\Install-V2.ps1 `
    -LegacyProjectRoot "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT" `
    -RunTests
```

The installer refuses to overwrite an existing `NIRA-V2` folder. It creates only that new directory.

If scripts are blocked, use explicit scoped invocation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\NIRA-V2\scripts\Verify-Foundation.ps1 -IncludeDemo
```

## Repository layout

```text
NIRA-V2/
├── apps/Nira.V2.Demo/                 # Honest local progress demonstration
├── src/Nira.V2.Contracts/            # Typed contract and response document
├── src/Nira.V2.Runtime/              # Agent loop, deterministic workflows, checkpoint stores
├── src/Nira.V2.Research/             # Evidence-aware response-document builder
├── tests/Nira.V2.Tests/              # Deterministic workflow and document tests
├── tests/Nira.V2.AgentLoop.Tests/    # Dynamic agent, failure and authorization tests
├── scripts/                           # Safe install / independent checks
└── docs/                              # Architecture, migration gates, security
```

## What the demo actually proves

The demo uses a **scripted synthetic model** and fake read-only research tool. It proves that NIRA's *new runtime can execute multiple decisions, record actual progress, and persist a completed run*. It does not claim to answer research questions or validate photographs.

## What remains protected

No code in this release modifies `NIRAAgent/`, `NIRAAgent.UI/`, memory stores, persona files, the ELVARA bridge, existing Git history, API keys or user settings. Legacy removal happens only after parity and rollback tests.

See `docs/ARCHITECTURE.md`, `docs/MIGRATION_AND_RETIREMENT.md`, `docs/RELEASE_GATES.md` and `docs/SECURITY.md`.

## Optional CI

A GitHub Actions template is provided at `docs/CI_WORKFLOW_TEMPLATE.yml`. GitHub does not discover workflows nested inside `NIRA-V2/`; after review, place the template at the **repository root** `.github/workflows/nira-v2.yml`. The bootstrap installer intentionally does not change your repository's root CI configuration.
