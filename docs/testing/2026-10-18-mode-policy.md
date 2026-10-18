# Five download modes — 2026-10-18

Task 5 of the approved Windows stabilization plan. Task 4's destination validation, checkpoint assignment dictionary, repair/download gates, and offline catalog were reviewed inline after a manual model switch; no important issue was found. This was not an independent agent review.

## Behavior

- `RunMode` exposes five values with their existing numeric identities: All=1, Remaining=7, PartialFiles=8, IntegrityFailures=9, DownloadErrors=11. Retired enum values and their executable selection branches are removed.
- Cloud inventory refresh does not expand the selected mode. DownloadErrors requires an error for the same cloud version; new paths, changed versions, and user cancellations are excluded. PartialFiles still selects only files with local partial data.
- A DownloadErrors job rechecks cloud metadata before preparing its payload. If the version changed after enumeration, the job is excluded without moving local bytes or overwriting prior error evidence; refreshed inventory metadata is retained for a later appropriate mode.
- Automatic passes change All to Remaining. Other modes retain their identity. The shared session policy prevents an IntegrityFailures automatic repeat while allowing later manual launches. Existing bounded transfer retry logic is unchanged.
- Progress counters describe the selected work in the current pass. The special cumulative counters for Continue are removed from both engines.
- Pass history continues to store and read Mode strings. Removed and unknown modes display a localized legacy-mode label; loading does not rewrite their journal entries.

## Verification

Before implementation, focused tests failed because a previous-version error selected a new cloud version, refreshing expanded DownloadErrors, and an error-only API pass downloaded an excluded changed file. A further regression reproduced a cloud version changing between enumeration and link preparation.

Focused tests then passed for selection, session policy, refreshed error/partial passes, metadata changes with and without partial data, repeated manual integrity passes, successful recovery from checksum failure, and unchanged historical mode strings. Existing filter/history/progress tests were moved to supported modes while retaining coverage for timeouts, partial bytes, cloud failures, verified local files, Stop, and changed versions.

`ApiTests.PipelineTests` now runs variants 0–4. The previously unreachable 30+30 connection scenario also has a focused `PipelineCapacityTests` entry point and passed on x64. Its generous 10-second stage budgets test connection capacity independently of the separate deadline scenarios.

Full x64 and x86 suites passed with exit code 0 in the ordinary Windows session. Results are recorded in `modes-2026-10-18/`. Both full suites include the 30+30 scenario. Checks use mock network responses and temporary local data; live cloud changes are not exercised.
