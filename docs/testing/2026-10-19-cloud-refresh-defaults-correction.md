# Cloud refresh defaults correction — 1.2.0

The owner requested restoring both cloud-list refresh checkboxes to off and explicitly chose to keep release version 1.2.0. The original 1.2.0 enabled refresh on manual Start without confirming that change; this correction restores the prior default.

- New profiles show both options unchecked; the nested option is disabled.
- Older saved settings are corrected once with `CloudRefreshDefaultsVersion=1`. Both refresh fields are cleared, without resetting timeout, retry, update, account, folder or history settings. This one-time correction applies to older saved profiles, including profiles with explicitly enabled refresh; it is the owner's requested reset of these two fields.
- A subsequent manual refresh choice is saved and retained on later launches. The marker is persisted through the existing settings-save path; form initialization does not introduce an unsafe or unconditional write to a damaged settings file.
- A missing saved inventory is still scanned automatically; this fallback is covered by `ApiTests.Run`. No cloud-list deletion or history migration is performed.
- Mode remains Remaining; normal retry/update checks and transfer defaults are otherwise unchanged.

`ScanControlsTests.Run` failed on the unwanted fresh-install default before its correction. `RefreshDefaultsMigration` then failed because existing checked settings were retained. Both pass after the implementation, including unchanged unrelated saved settings and persistence of a later manual choice. Focused UI tests pass in x64 and a real 32-bit PowerShell process for x86.

Full local suites and GitHub Actions are required before replacing the release assets. The corrected distribution retains the ten-file allowlist; personal settings, tokens, history and runtime logs are excluded. RU/EN README, guides, embedded guide constants and release notes describe the corrected options.

Because the owner retained the same version, users who already installed the original 1.2.0 must download the corrected ZIP manually and replace the application files while keeping `data`. Same-version automatic update detection cannot deliver that replacement. The release notes explicitly explain this.
