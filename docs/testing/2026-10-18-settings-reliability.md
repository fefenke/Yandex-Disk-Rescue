# Settings and token persistence — 2026-10-18

Task 2 of the Windows stabilization plan. .NET Framework 4.7.1 and the existing JSON/DPAPI formats are retained.

## Behavior

- Settings and encrypted token bytes are written to a unique temporary file in the same directory, flushed with `Flush(true)`, then committed with `File.Replace` (or `File.Move` for a new file). There is no delete-and-move replacement fallback.
- A settings replacement retains the previous valid JSON as `settings.json.bak`. Loading tries the primary, then the backup, then defaults, without writing files.
- Saving after recovery preserves a corrupt primary as `settings.json.corrupt-<guid>` and leaves the existing backup intact. Unreadable files are not classified as corrupt by the save operation: I/O errors propagate.
- Initialization suppresses UI save handlers. Recovery and save failures appear in a separate persistent, localized notice. Error text includes no file contents, token, or raw exception text. Starting a pass stops if settings cannot be saved.
- Tokens remain DPAPI CurrentUser ciphertext. No token backup is created; explicit removal deletes the token file.
- The project already includes `src/*.cs`; new source files need no additional csproj entries.

## Verification

Before implementation, `SettingsStoreTests.UiFailure` failed against the old writer: `Settings save failure was not reported to the caller.`

Focused tests passed for:

- Unicode roundtrip and exclusion of `ApiToken` from serialized JSON.
- Locked and read-only primary files: failure reported, previous settings and backup retained, temporary file removed.
- Locked backup: final replacement fails without changing primary or backup.
- Torn primary, both files corrupt, missing primary, and unreadable primary; recovery reports a warning and does not write files.
- Saving after recovery preserves the corrupt original and the valid backup.
- UI error visibility, persistence through translation, successful retry, and read-only initialization after recovery.
- Encrypted token replacement, locked/read-only failures, absence of leftover token copies, and explicit deletion. DPAPI test ran outside the sandbox in the Windows user session, with temporary test data.

Full x64 and x86 suites both passed with exit code 0 outside the sandbox. Both builds were compiled from this change; each suite ran in the corresponding executable architecture. Results are recorded in `settings-2026-10-18/`.

## Limits

Tests simulate file locking and read-only attributes; they do not simulate physical power loss, disk exhaustion, storage firmware failure, or every filesystem. A process killed before replacement can leave its unique temporary file; subsequent loads ignore it. Filesystem and storage durability guarantees still apply. Visual checks across Windows versions and DPI settings remain part of the final validation stage.
