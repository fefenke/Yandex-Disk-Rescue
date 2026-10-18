# Review of the updater and settings changes — 2026-10-18

Scope: commits `69bf4a3` and `519dcac`, checked after a manual main-model switch. This is an inline review, not an independent agent review; the user requested no subagents. The approved Windows stabilization plan remains the reference.

## Finding fixed

The updater's `ReadCancelled` observes a later read fault when cancellation wins `Task.WhenAny`. A read task that had already completed with a fault could win `WhenAny` just before cancellation became visible. `ThrowIfCancellationRequested()` would then throw without awaiting or observing that fault. A focused regression forces a pre-faulted read and pre-cancelled token, then checks `TaskScheduler.UnobservedTaskException` after collection. It failed against the prior implementation and passed after the fix, which observes the read fault before throwing cancellation.

## Other reviewed boundaries

- A failed settings replacement keeps both the current valid JSON and its previous backup. Recovery reads without writing; an explicit later save retains a corrupt primary under a unique name.
- A failed token replacement keeps the existing DPAPI ciphertext. Token removal remains explicit.
- Updater staging cleanup checks that it owns a generated directory and rejects reparse points; successful packages remain available for the updater helper.
- The updater still enforces the SHA-256 ZIP digest, allowlisted entries, size limits, and stable release selection.

The focused race, downloader, and settings-storage tests passed after the correction. Full x64 and x86 suites also passed with exit code 0 in the ordinary Windows session, using binaries built with this correction. The next task's preservation tests were added to source while these binaries ran; those tests are verified separately in Task 3.
