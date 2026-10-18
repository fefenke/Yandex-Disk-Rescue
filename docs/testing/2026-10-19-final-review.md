# Final Windows stabilization review — 2026-10-19

Scope: the stabilization changes on `work/windows-stabilization` since baseline `234fea2f415007a698d0faa834e1136f3d3891fd`. Review performed in the main conversation after the user's manual model switch, without subagents. This shares prior context and is not an independent review.

## Finding and resolution

**Cached destination ownership was not persisted after a conflict was resolved.** With cloud entries `disk:/A.bin` and `disk:/a.bin`, an ambiguous existing partial file correctly blocks both. After the user moves that partial aside, a pass using the saved inventory assigns distinct destinations and downloads both files. Previously, those assignments were only retained in memory: the next cached pass rejected the downloaded files as ambiguous.

`DestinationPathsTests.CachedResolution` reproduced the failure before the fix: “A cached pass forgot resolved destination ownership and blocked completed files.” The API engine now detects newly established or changed assignments and saves the inventory checkpoint before download preparation. Scan checkpoint fields are retained. Unchanged assignments need no write.

The regression checks successful download and reuse on a subsequent cached pass, distinct contents for case-colliding cloud names, preservation of the user's moved bytes, failure before requesting download links when the inventory cannot be replaced, and successful reuse of an unchanged inventory even while it is locked against replacement. Focused destination tests and both full suites pass.

## Review coverage

- Updater cancellation, late read failures, package cleanup and UI start/close controls.
- Atomic settings/token replacement, backup recovery and visible save failures.
- File preservation cancellation before durable intent and recovery after intent.
- Path ownership across cached lists, refresh and interrupted scan; conflict gating before repair or downloads.
- Same-version download-error selection, metadata changes and exceptional result draining; automatic repeat policy.
- API-only facade, cancellation, legacy fields/history retention and removed client paths.
- Bounded test runner, documentation and package compatibility evidence.

No additional important defect was identified in those reviewed changes. This is not a guarantee that every application path is defect-free.

## Final verification

- Rebuilt and packaged x64 and x86 sequentially with `build.ps1 -Package -OutputDirectory dist/verify-<architecture> -Platform <architecture>`.
- Ran each real executable via `tests/run-tests.ps1 -OutputDirectory dist/verify-<architecture> -Platform <architecture>` in the ordinary Windows session. Both full suites returned PASS, exit code 0. Reports: [x64](stabilization-2026-10-18/x64.txt), [x86](stabilization-2026-10-18/x86.txt).
- Scanned 2372 report/settings files (`summary.json`, `settings.json`, CSV) only within the two new synthetic suite roots: none contained the known credential or signed-link markers. See [redaction evidence](stabilization-2026-10-18/redaction.txt).
- Compared final ZIP entry paths with rebuilt baseline packages: ten identical paths per architecture; distribution names unchanged.

The earlier runner, launcher and embedded-guide checks remain applicable; their implementation did not change during this review. No live account or production update installation was used. Other Windows versions, all DPI combinations, physical power loss and very large inventories remain outside this verification. See the [main report](2026-10-18-windows-stabilization.md) for the complete scope and limits.

Tasks 0–7 are complete. The branch and local packages are retained; no push, merge, release or installation was performed. .NET migration remains separate work.
