# Phase 1B — Direct Ollama Cloud transport (no local Ollama installation)

## Why this is a separate provider transport

The local CLI model `gpt-oss:120b-cloud` maps to `gpt-oss:120b` for the
Ollama direct-cloud API. The direct API endpoint is `https://ollama.com/api/chat`
and requires `Authorization: Bearer <key>`.

**Ollama Cloud does not currently support structured output schemas.** V2 therefore
omits `format` for cloud calls, instructs the model to produce JSON, and strictly
parses/validates the returned decision. No JSON or incomplete responses => no success.
The previous local transport keeps its schema-enforced format.

## Run on Windows PowerShell

```powershell
Set-Location "$env:USERPROFILE\Documents\MULTI-LANG-PROJECT\NIRA-AGENT\NIRA-V2"
$env:NIRA_V2_PROVIDER = 'cloud'
$env:NIRA_V2_MODEL = 'gpt-oss:120b'  # or any compatible cloud model
Remove-Item Env:NIRA_V2_OLLAMA_URL -ErrorAction SilentlyContinue
dotnet run --project .\apps\Nira.V2.LivePilot -- 'Explain an agentic execution loop in three short paragraphs.'
```

The pilot first reads the existing `NIRA_OLLAMA_API_KEY`, then `OLLAMA_API_KEY`,
from its process environment or Windows user environment. It does NOT print the
key or store it in run checkpoints. **Never paste your key into chat.**

**Security:** The pilot has no tools or web access, does not load stored identity,
memory or permissions, and writes unencrypted test questions/answers to
`%TEMP%\NIRA-V2-PilotRuns`. Use non-sensitive prompts only. The code permits
Authorization headers only for the fixed HTTPS host `ollama.com`. The pilot
turns off redirects and cookies. Cloud calls can incur provider usage charges.

## Error codes

- `CLOUD_API_KEY_MISSING` — existing key not found in available environment
- `CLOUD_AUTH_REJECTED` / `CLOUD_PERMISSION_DENIED` — credential rejected
- `MODEL_NOT_AVAILABLE` — model unavailable under direct API or mistyped name
- `PROVIDER_RATE_LIMITED` — quota/rate restriction
- `PROVIDER_UNAVAILABLE` / `PROVIDER_NETWORK_ERROR` / `PROVIDER_TIMEOUT` — retry later
- `INVALID_PROVIDER_DECISION` — cloud model did not return valid JSON

## Acceptance gates

- 9 foundation + 10 agent-loop + 8 original provider + 11 cloud tests pass.
- Pilot compiles, uses an existing key, connects to official HTTPS Ollama Cloud.
- One real test question returns `RUN_COMPLETED` and `STATUS: Completed`.
- Legacy `NIRAAgent` and `NIRAAgent.UI` untouched.

Docs: https://docs.ollama.com/cloud ,
https://docs.ollama.com/api/authentication ,
https://docs.ollama.com/capabilities/structured-outputs .
