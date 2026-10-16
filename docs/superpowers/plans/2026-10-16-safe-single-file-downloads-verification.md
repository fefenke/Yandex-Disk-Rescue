# Local verification: safe copies and single-payload downloads

Date: 2026-10-16. Branch: `feature/safe-single-file-downloads`.

## Results

- Full self-tests passed for x64 (`dist/safe-final-x64`) and x86 (`dist/safe-next-x86`), including transfer retries, legacy segment recovery, queue budgets, API pacing, cancellation, history, updater checks and synthetic DPAPI token storage.
- After the destination index optimization and malformed-header recovery changes, focused SafeDownloadTests, RangeMapTests, SignedLinkTests and ApiPacerTests passed for x64 (`dist/safe-verified-x64`) and in an actual 32-bit PowerShell process (`dist/safe-verified-x86`).
- Final local outputs: `dist/safe-ready-x64` and `dist/safe-ready-x86`. Both compile successfully. The last UI-only changes distinguish an existing size-matched file from a newly downloaded file when the cloud supplies no hash, and clarify the API-only mode message.
- The final x64 output passed SafeDownloadTests and RangeMapTests on HDD E: in a new isolated `YDR-Safety-Test-<GUID>` directory. This includes forced termination of a dedicated child process after a 32 MiB checkpoint plus an unconfirmed tail. Recovery trusted exactly the confirmed 32 MiB. Synthetic files were removed after success.
- Independent read-only review findings were addressed and re-reviewed: deleted quarantine recovery, structurally invalid maps, custom legacy boundaries, internal-name collisions, and unresolved transaction ownership.
- `git diff --check` passes with `core.whitespace=cr-at-eol` for the repository's existing mixed line-ending convention. A scoped credential-pattern scan found no matching real-token patterns in sources or documentation.

## Covered safety cases

- Size before checksum; cloud MD5 takes priority over SHA-256. SHA-256 is used only when MD5 is missing. A successful SHA-256 cannot override a failed MD5.
- Existing files remain until the new payload completes. Old size/hash conflicts and failed new copies are preserved. Transaction recovery is tested before the old move, between moves, after promotion, and after external deletion of the old archived artifact.
- A failed cloud version stays blocked after rescanning, restarting or deleting its quarantined artifact. A changed cloud version may download again. Explicit retry is one pass, and identical preserved bad copies are not duplicated.
- Concurrent positional writes, full allocated length with zero confirmed progress, torn maps, unconfirmed bytes, corrupt extents, malformed header types and custom legacy range boundaries.
- Legacy resume, changed versions, unsupported/invalid Range, progressive retries, cancellation, and confirmed signed-link reuse.

## Limits

Physical power removal was not performed. Process termination and damaged-data tests do not prove behavior of a drive/controller that ignores flush commands. The former release's 2 GiB write benchmark is not a benchmark of the new range-map format.

When a cloud destination occupies another file's internal temporary name, the affected download fails safely with a conflict message; other files continue. This avoids treating another final file as scratch storage.

No release was published, no remote branch was pushed, and no running application or real backup was modified.
