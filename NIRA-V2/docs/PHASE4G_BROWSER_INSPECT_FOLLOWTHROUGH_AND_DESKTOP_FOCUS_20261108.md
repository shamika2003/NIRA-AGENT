# NIRA V2 Phase 4G — browser inspection follow-through and desktop activation

Baseline: `NIRA_AGENT_FULL_CODE(20261108-100001).md` / event log `nira-events-7684.log`.

## Confirmed observations

- `browser.navigate` succeeded in the live app, but no corresponding `browser.inspect` receipt was recorded before the rejected final response (`UNOBSERVED_SOURCE_URL`). Browser had loaded the requested site, but the compound user task was not completed.
- The exact `desktop.inspect` receipt succeeded (HWND `0x30518`). The following owner-gated `desktop.act` reached the Windows UIA backend, which returned `Dispatched=False`, `ForegroundVerified=False`. No focus change may be claimed.
- `filesystem.docx.inspect` succeeded and the displayed A4 210x297mm / 1-section / 1-paragraph information is now plausible. No new DOCX write or overwrite change is needed.
- Chromium's "Restore pages?" popup is a sign of an unclean previous browser shutdown. The dedicated persistent browser profile and cookies must not be deleted just to hide the popup.

## Scope of this patch

1. When user explicitly requests `visit` followed by `inspect`, and this run has a successful host-audited exact navigation, schedule the existing separately consented read-only `legacy.browser.inspect_active` before drafting the response. This is not an additional agent or any grant of browser control. It never replays navigation or retries a failed inspection.
2. Correct model recovery instructions after an unobserved URL: only exact current-turn verified navigation URL may be repeated; untrusted page links and invented citation URLs remain forbidden. Existing strict URL/citation checking is unchanged.
3. Request Chromium's historical `--disable-session-crashed-bubble` switch for NIRA's separate profile without modifying any profile files. The installed Chromium/Chrome version may ignore this switch; do not claim it guarantees dismissal. Always close browser via NIRA's session shutdown when available.
4. For a separately owner-approved, exact observed HWND activation, try standard Win32 focus once on the worker and (if it fails) once on the NIRA WPF dispatcher thread, revalidating HWND and PID. Do not use `AttachThreadInput`, synthetic input, elevation or forced focus. Windows may still refuse focus; then report no verified change.
5. The old authorization brokers, per-turn consent flags, file safety gates, memory and legacy owner remain unchanged. No Git or legacy retirement.

## Windows acceptance

- Close NIRA first, AuditOnly, then ApplyAndVerify. Every existing NIRA V2 console regression project and legacy regression suite must pass, then WPF debug build.
- Run the existing preview with the same previously consented browser and desktop flags.
- Ask: `Visit https://example.org/ in the browser, then inspect the active page.` The diagnostic event log should contain both successful `browser.navigate` and `browser.inspect`. A failure in the second step must not be reported as inspected.
- Ask: `Activate application Code window after inspecting its controls.` Exact desktop.inspect must precede owner Allow Once and desktop.act. Windows may refuse the focus move, in which case the response must say so. Never retry automatically on an uncertain outcome.
- No overwrite, delete, shell command, arbitrary browser navigation, or new authority.
