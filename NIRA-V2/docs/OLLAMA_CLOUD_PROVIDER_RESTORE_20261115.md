# NIRA V2 — Return to Ollama Cloud (2026-11-15)

- Both the existing V2 agent loop and legacy cognition client use Ollama Cloud's OpenAI-compatible **`https://ollama.com/v1/chat/completions`** endpoint.
- Default V2 model **`gemma4:31b`** (restores original NIRA adaptive Gemma role). Legacy deep role defaults to **`nemotron-3-ultra`** and vision uses Gemma 4. Direct Cloud API model IDs omit the CLI-only `-cloud` suffix; both model IDs are documented by Ollama. Override V2 with `Start-NiraOllama.ps1 -Model <ollama-cloud-model-id>`; choose a compatible cloud model available to your account.
- Direct cloud access requires an API key. The launcher requests it in a hidden prompt, saves it as a *separate* Windows CurrentUser DPAPI encrypted blob under `%LOCALAPPDATA%/NIRA/Secrets/ollama-cloud-key.dpapi`, and sends it only via process environment variables (no key in project files or Git). Existing NVIDIA blob is untouched. The optional `-SessionOnlyKey` avoids persistence; `-ForgetSavedKey` deletes **only the Ollama key blob**, not other state.
- Ollama `reasoning_effort=low` for clearly social turns and `medium` otherwise. NVIDIA-only request fields are not sent to Ollama. The legacy client likewise uses `reasoning_effort=medium`.
- The existing host-owned streaming reader, verified final publication, Piper, authority, branches, goals, memories, original personality YAML, and Phase 4H.6 state remain unchanged. NVIDIA implementation remains available *only as an explicit compatibility provider*; there is **no automatic fallback** or double execution.
- V2 still has incomplete character-state mutation/memory-formation parity. The optional `-LegacyOnly` launcher flag runs the original NIRA Executive on Ollama, to compare original personality and behavior without changing persistent state or source. `-LegacyOnly` does not invoke the V2 agent loop; leave it off when testing V2 Phase 4H.6 or later.
- Starting NIRA needs no local Ollama daemon or 120B model download; this uses paid **Ollama Cloud**.

## Windows acceptance

1. Close NIRA. Run installer `-AuditOnly`, then `-ApplyAndVerify`. It builds WPF, runs every V2 test project, the legacy 522-regression suite, and checks the new Ollama endpoint suite. Never run `ApplyAndVerify` while NIRA is open.
2. Run `.\NIRA-V2\scripts\Start-NiraOllama.ps1 -AllowPersonalContext -AllowLifecycleContext -AllowLifecycleInspect`. Enter Ollama API key once. Close and relaunch to verify DPAPI reuse.
3. Check ordinary social messages (`Hey Nira`, `oo babe 😄`), one technical answer, SSE streaming, Piper final-only voice, and an authorized read-only branch inspection. Verify NIRA does not invent permission receipts.
4. If V2 still lacks original interpersonal behavior, run `.\NIRA-V2\scripts\Start-NiraOllama.ps1 -LegacyOnly` for an original-Executive comparison. Do not treat legacy behavior as proof V2 parity.
5. Verify a 401/403 displays an Ollama credential issue, 429 is not automatically replayed, and 400/404/5xx are reported as incomplete. Never auto-retry uncertain side effects.

No existing persistent files, ELVARA bridge, or Git operations are changed by the patch.
