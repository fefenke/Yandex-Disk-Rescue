# Live-account verification — 2026-10-19

Local x64 stabilization build at `4e6649f`, before the 1.2.0 version/defaults update. The user entered credentials directly into an isolated portable app, selected cloud folders and operated the UI. Results below were read from local pass summaries and CSV verification fields; original account IDs, cloud paths, file names, settings, token vault and raw reports are not included in this repository.

| Scenario | Observed result |
| --- | --- |
| Small folder, Remaining | 29 downloaded, 29 available, zero remaining/errors; 7.59 seconds |
| Restart, Remaining | Zero downloaded, 29 available, zero remaining/errors; 0.60 seconds |
| Small folder, All | 29 processed, zero downloaded, all 29 `md5_verified`; zero hash mismatches; 0.37 seconds |
| User opens downloaded photos | User confirmed normal operation |
| Larger folder, Stop | `stopped`; 11 available out of 353; queued/active work retained for another pass |
| Larger folder, resume Remaining | 255 additional downloads; 266 available, 87 remaining |
| Larger folder, All | Six additional downloads; 272 available, 81 remaining; all 272 ready files `md5_verified`, zero hash mismatches |

The final remaining items were 64 `Download HTTP 500`, 14 mapped-download idle timeouts and three mapped-download read failures. These are recorded failures, not evidence of complete download success. The acceptance criterion is retaining successful files and progress, handling Stop and permitting further attempts for unavailable files. Downloading every unavailable cloud item is not an acceptance criterion and is not promised by this release.

The live-test profile used 30-second network timeouts, disabled automatic repeats and disabled update checks, so manual runs could be inspected separately. That profile and its data are not packaged. Release defaults remain the recovery-oriented 3-second link/idle timeouts, automatic retries and update checks; fresh installations refresh the cloud list on manual Start, without rescanning on every automatic repeat.

This verifies the observed account, files, network and available Windows installation. It does not exercise every server failure or Windows/DPI configuration. UI language switching, production self-update installation and physical power failure were not verified during these live runs.
