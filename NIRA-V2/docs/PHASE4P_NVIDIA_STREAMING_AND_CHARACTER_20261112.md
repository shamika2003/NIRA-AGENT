# NIRA V2 — NVIDIA streaming, character and key stabilization (12 Nov 2026)

Focused stage between Phase 4H.6 and 4H.7. No changes to NIRA character
stores, SQLite goals/branches, long-term memories, permission authority, Piper
speech, WPF UI design, ELVARA/TradeAI bridge or Git.

## Changes

1. `Start-NiraNvidia.ps1` asks for the NVIDIA key once, then stores ONLY a
   Windows CurrentUser DPAPI-encrypted SecureString blob at
   `%LOCALAPPDATA%\NIRA\Secrets\nvidia-cloud-key.dpapi` for future launches.
   `-SessionOnlyKey` avoids saving a newly entered key; `-ForgetSavedKey` removes
   only this saved encrypted credential and exits.
2. V2 model decisions use real NVIDIA Chat Completions SSE, with strict byte,
   finish-reason and tool-call validation. `reasoning_content` is never shown.
   An interrupted or malformed response is never treated as a confirmed answer.
3. The WPF bubble renders **provisional text** as it streams. Only the final
   validated response is archived and passed once to the original
   SpeechChunker / VoiceQueue / Piper. Voice begins after final validation to
   avoid speaking partial, incorrect, or rejected text.
4. A conservative fast mode for clearly short social greetings disables
   thinking and uses a less robotic sampling temperature. This does NOT
   hardcode NIRA's replies, bypass model decisions or disable tool permissions.
   Ambiguous, complex and ongoing work still receives full reasoning.
5. The original `nira_personality.yaml` remains the sole authoritative stable
   personality definition, supplied before tool policy instructions. Its
   personal state/memory context still requires the original consent flag.
6. Direct V2 host replies now emit the correct completed-turn event so the
   original conversation archive sees their final answer exactly once.
7. The foundation static audit now recognizes only the already-reviewed
   migration adapters/tests that refer to the original NIRA project.

## Limits

- Streaming improves first visible text time but does not guarantee NVIDIA
  server speed or lower total completion time.
- Legacy-executive model calls remain buffered during the migration.
- Same-user processes can decrypt DPAPI data; another Windows user or PC must
  reenter the key. Do not copy the key blob to Git or the source export.
- No modification capability is granted by this package.
- Live WPF speech, approval, crash/restart, and model quality checks are
  required before calling this stabilization stage fully accepted.

## Launch after a successful audited install

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File '.\NIRA-V2\scripts\Start-NiraNvidia.ps1' -AllowPersonalContext -AllowLifecycleContext -AllowLifecycleInspect
```

## Manual Windows acceptance

- First run: enter NVIDIA key invisibly; close NIRA and launch again; should
  reuse the encrypted key without prompting. `-ForgetSavedKey` must remove only
  that secret and must not launch the app.
- Ask `Hey Nira, how are you?`, then `oo babe 😄`. Check natural identity,
  progressive visible output and no reasoning or tool instructions in chat.
- Turn Piper ON in NIRA Settings. Listen for one complete final answer without
  repeated sentences or truncated words. Test interruption by a new message.
- Ask an authorized read-only observation and verify the host tool receipt,
  not just the model's prose.
- Restart and inspect an existing test branch; it must retain its UUID, SQLite
  status and evidence count. No goal/branch modifications may happen on a read.
- Do not delete NIRAExecutive or the original personal stores. Continue toward
  Phase 4H.7 only after Windows build and live tests pass.
