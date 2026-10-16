# Safe copies and range maps implementation plan

> Execute inline using executing-plans and TDD. The user explicitly waived further document approval and requested implementation of the discussed design.

Goal: preserve user files, validate MD5/SHA-256 correctly, suppress repeated corrupt downloads per cloud version, and write ranges into one crash-recoverable payload.

Spec: ../specs/2026-10-16-safe-single-file-downloads-design.md

Constraints: C#5/.NET4.7.1, x64/x86, API only, no changes to active application/data or GitHub. Keep32MiB/15s checkpoints and existing queue/API behavior. Public release layout unchanged.

- [x] Add failing integration tests for SHA fallback, preserved old files, corrupt-copy ledger, deleted quarantine and changed cloud version.
- [x] Implement CloudIntegrity and preservation transactions, extend models/history/UI with verification evidence and explicit retry mode; run focused tests.
- [x] Add failing range-map tests for concurrent positions, unconfirmed tails, torn maps, partial corruption, migration and unsupported Range.
- [x] Implement single-payload map and legacy migration; integrate progress/catalog/preparation; run focused transfer tests.
- [x] Review failure recovery at every move/flush boundary; test synthetic process termination and HDD behavior, without power removal.
- [x] Update RU/EN guide and run full x64/x86 tests; independent read-only review, fix findings, report local builds without publishing.

Review focus: unknown hashes and invalid metadata; two files with same path/size; deleted preserved artifacts; full-length sparse payload with missing ranges; interrupted preservation/migration with no space or sharing violations.
