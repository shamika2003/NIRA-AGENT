# Phase 4G — Browser evidence and owner-approval diagnostics (November 8, 2026)

This repair is limited to the existing V2 canary. Production NIRA, old personal identity, memory, goals, voice, ELVARA/TradeAI and legacy executive remain untouched.

## Browser evidence

The existing legacy browser service produces an audited success when it navigates to an exact user-provided HTTPS URL. The V2 wrapper attaches `verifiedTargetUrl` to the vaulted observation **only** after a successful matching legacy `browser.navigate` receipt. V2 answer validation accepts this URL only if the current user turn explicitly contained the same normalized HTTPS URL and the receipt's capability, origin and success markers match. Untrusted page hyperlinks and unrelated session addresses are not admitted. `web.search` citation ownership remains independent and unchanged.

## Desktop activation

The activation capability requires a verified exact HWND observed by `desktop.inspect` in the same run. A generic UIA accessibility observation alone does not authorize activation. A missing HWND is now reported as a precondition failure, not incorrectly described as an owner Deny. An actual Allow Once decision still comes solely from the existing NIRA approval broker. No clicks, typing, shell or broader application scope are granted.

## DOCX grounding

WordprocessingML section geometry uses **twips**: 1440 twips per inch, 25.4 millimetres per inch; A4 portrait is about 210 × 297 mm, not 119 × 168 mm. DOCX XML does not establish the actual rendered page count. The opted-in DOCX preview available to reasoning is increased from 1,100 to 3,000 chars to retain more of the existing audited metadata; the model is explicitly instructed to avoid unsupported page count claims.

## Acceptance gates

- All changed files match exact audited source baselines or already-installed hashes (LF/CRLF/final-line variants only).
- New payload hashes match the package manifest. No Git operations.
- Every V2 console test, the 522-check legacy regression suite and the WPF UI build pass on Windows with NIRA closed.
- Browser navigation + inspection yields the grounded visited address and content. Fake website links remain rejected.
- Desktop act cannot dispatch without exact same-run HWND and owner's Allow Once.
- A fresh write **denied via actual permissions UI** leaves no file behind. Filenames containing `deny` are not permission decisions.

This patch is not complete Phase 4G parity. Broader desktop/browser interaction and multimodal migration remain pending.
