# Windows stabilization — 2026-10-18

Branch: `work/windows-stabilization`, based on `234fea2f415007a698d0faa834e1136f3d3891fd` (1.1.2). Tasks 0–7 and the final review after a manual model switch are complete as of 2026-10-19. This is local implementation and verification; no release has been published.

## Delivered behavior

| Area | Result | Evidence |
| --- | --- | --- |
| Updater | Stable releases, cancellable download, bounded read/overall waits, discarded unused packages, observed late faults | [Updater](2026-10-18-updater-reliability.md), [review](2026-10-18-storage-updater-review.md) |
| Settings/token | Atomic persistence, valid backup recovery, preserved corrupt primary, visible save failures and no plaintext token backup | [Storage](2026-10-18-settings-reliability.md) |
| Preservation | Cancellable hashing before move intent; recoverable move transaction after intent | [Preservation](2026-10-18-preservation-cancellation.md) |
| Local paths | Stable saved ownership across refresh/interruption; conflict rejection before writes/repair | [Destinations](2026-10-18-destination-conflicts.md) |
| Modes | Five supported modes; same-version errors only; independent cloud refresh; All narrows on automatic repeat; integrity mode allows one pass per manual start | [Modes](2026-10-18-mode-policy.md) |
| API-only | Removed client execution, process management, Cloud Files dependency and probe; old settings/history retained | [API-only](2026-10-18-api-only.md) |
| Guides | RU/EN instructions and embedded guide match current behavior; historical release notes retained | `GUIDE*`, `README*`, `src/GuideText.cs` |
| Test runner | 900-second default deadline, only owned test process killed, separate timeout error, fresh report required, requested build directory/architecture retained | `tests/runner-tests.ps1` |

Review after Task 5 also found that exceptional cleanup could overwrite error history for a changed cloud version already excluded from DownloadErrors. The regression was reproduced before the fix; normal and exceptional result recording now share the exclusion policy. See the API-only report.

The final review found that a cached pass could resolve a destination conflict and download files without persisting their newly established path ownership. A later cached pass then rejected those completed files as ambiguous. Newly established assignments are now persisted before downloads; a write failure stops the pass, and unchanged assignments do not trigger a rewrite. The regression failed before the fix and passes afterward. See the [final review](2026-10-19-final-review.md).

## Task 7 verification

- `tests/runner-tests.ps1` passes with a one-second synthetic deadline. It checks successful/failed runs, rejection of a stale report, termination of the owned hanging process, survival of a second process using the same executable, and x86/custom-path build argument forwarding. No real 15-minute wait is required. The deadline applies to the self-test executable, after any required build.
- The Windows workflow now passes its matrix architecture to the runner and runs the short runner regressions once in the x64 job. GitHub Actions has not been executed for this local commit.
- Both x64/x86 builds compile and package. Embedded guide constants were compared with the plain-text rendering of their corresponding Markdown files; both match.
- `Start Yandex Disk Rescue.cmd --check` passes in both output folders: Windows build 26200, Framework release 533509. File/product version is 1.1.2.0 for both executables.
- ZIPs were compared with packages rebuilt from baseline `234fea2`. Both retain exactly the same ten file paths. Archive names remain `YandexDiskRescue-win-x64.zip` and `YandexDiskRescue-win-x86.zip`. User data, source and `self-test-results.txt` are absent. See [package comparison](stabilization-2026-10-18/package-comparison.txt).
- Final full suites PASS, exit code 0 for both real [x64](stabilization-2026-10-18/x64.txt) and [x86](stabilization-2026-10-18/x86.txt) executables under the new runner. The suites include the 30+30 pipeline, metadata-exclusion exceptional-drain regression, API-only facade, updater cancellation, storage and preservation checks.
- After the final-review fix, both packaged architectures were rebuilt and both full suites passed again, including the cached destination ownership regression. The linked x64/x86 reports represent these final runs. Both ZIPs still contain the ten baseline paths.
- PASS: 2372 synthetic reports/settings files from the two final-review suite roots contain no test-secret, sensitive-credential or signed-link lease markers. See [redaction result](stabilization-2026-10-18/redaction.txt) and [runner result](stabilization-2026-10-18/runner.txt). This checks those known synthetic markers; it does not certify arbitrary user logs. Existing tests also verify that saved signed links remain in memory and crash diagnostics omit exception messages.

The ZIP consists of the EXE/config, RU/EN READMEs, license, launcher and four preview images. Full guides remain embedded in the executable and available in the repository, matching the baseline distribution schema.

## Verification limits

Tests ran in the available ordinary Windows session on build 26200 with Framework release 533509; x86 checks run in the real x86 executable. DPAPI requires the ordinary user key store and does not work in this sandbox. Separate Windows 10 1709 and later-version installations were not tested. Existing UI checks include logical scaling/field alignment and initial screen fit; every monitor, DPI, font and mixed-monitor combination was not tested.

Network responses, accounts and credentials are synthetic. No live Yandex account, real cloud availability, production GitHub update installation or installed Yandex Disk client was exercised. Physical power removal, disk exhaustion, arbitrary drive/controller flush behavior and very large cloud inventories were not simulated in this stage. Earlier HDD/process-kill measurements remain historical evidence with their original scope.

Local previews, fixtures and baseline build artifacts remain in the ignored plan scratch folder or temporary test directories. The user's installed app, data and active downloads were not changed. No push, merge, release or installation was performed.

The main-model reviews share conversation context and are not independent agent reviews. The agreed final broad review is complete, with its finding fixed and verified. .NET 10 migration, large-inventory storage redesign and moving the entire test suite into a separate project remain separate work.
