# Signed-link refresh and archive-name recovery

The downloader permits one shared signed-link refresh per file attempt after download-server HTTP 401, 403 or 410. All readers using the old link finish before fresh metadata and a new link are requested. Only an unchanged cloud version may reuse saved ranges. HTTP 404, ordinary timeouts, integrity errors and local write errors do not request this refresh. A second rejected link ends this file attempt.

`SignedLinkTests` exercises same-pass resume, bounded rejection, completed-range reuse, changed cloud versions, cancellation during refresh, next-pass fallback, and exclusion of signed links and OAuth credentials from persistent state or download requests.

`ArchiveNameMigrationTests` exercises restoring the ordinary archive name, recovery before and after the directory rename, preservation of history evidence, and exclusion of personal files, foreign owners, cloud path collisions, unresolved preservation operations and cancelled preparation. Migration records its intent before renaming, updates preservation references and retains a path alias for history recovery. A pending migration prevents downloads until recovery completes. A nonempty ordinary-name directory is never merged or overwritten.

Targeted regression tests failed against the previous implementation and passed after the changes. Full x64 and x86 self-tests passed on 2026-10-17, including DPAPI token round-trip, interruption/recovery, integrity checks and the new regression cases. The sandboxed runs reached the DPAPI test but could not access its Windows user profile; complete reruns in the normal user context passed. Independent read-only code review found no Critical or Important issues. Simulated interruptions do not constitute a physical power-cut test.

## Subsequent local-preparation performance fix

`FilePreserver.CheckDestination` previously enumerated preservation state and reread every `*.root` setting for every file. Roots are now loaded once per preserver/pass, and every successful `ArchiveRoot` operation updates the same cache under its existing lock. New passes reload the configuration. File integrity checks and range-map validation are unchanged.

The added `PreservationRootCacheTests` failed before the fix and passed afterward. It verifies destination protection with the configuration locked after initial loading, reload on the next pass, and registration of an archive created during the current pass. Six targeted suites passed after this fix: `PreservationRootCacheTests`, `SafeDownloadTests`, `ArchiveNameMigrationTests`, `RelocatedArchiveRepairTests`, `PreparationStopTests`, and `StartupReliabilityTests`. Both architectures compiled; the full x64/x86 runs above predate this additional performance fix. Independent review found no Critical or Important cache regressions.

A local read-only benchmark of 500 calls to the same `CheckDestination` method measured 16,190.24 ms before and 35.71 ms after, using the same preservation-state directory. This is a measurement of one operation, not a total-pass benchmark or a guarantee for other machines. The running installed application was not replaced.

## Release 1.1.0 validation

After the final preparation-cache change and version update, complete x64 and x86 self-test runs both passed in the Windows user context on 2026-10-17. This includes all new cache, archive-name and signed-link cases as well as existing recovery, integrity, cancellation, updater and DPAPI checks. Publication uses an explicit source/documentation allowlist; runtime history, credentials, logs and local repair helpers are excluded.
