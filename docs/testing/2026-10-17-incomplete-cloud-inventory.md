# Incomplete cloud inventory recovery

The regression test first failed on the published 1.1.1 code because a failed folder was persisted with `Complete=true`.

`InventoryCompletenessTests` exercises an unreadable folder alongside successful downloads, repeated failure without rescanning successful folders, recovery and clearing the outstanding-error count, old version 1/2 caches incorrectly marked complete, Stop during a resumed folder, and a later-page error followed by insertion before the old offset. Saved paths suppress duplicates. A separate case refreshes metadata of an existing file after an incomplete scan and verifies that the final metadata save retains the unresolved folder and incomplete marker.

Cache version 3 records failed folder paths independently of pending/active traversal. Completion requires all three to be empty. Failed folders are attempted once per scan; a later pass resumes their first page. Healthy completed folders are retained. Old error-bearing caches cannot identify all omissions, so the first ordinary Start rebuilds their cloud list while retaining local files and download history. Interrupting that rebuild leaves a resumable version-3 checkpoint. Healthy old lists are reused normally.

Automatic repeats include unresolved enumeration errors even when there are no matching download jobs. Enumeration-only failures use the existing bounded, cancellable backoff. Normal successful passes retain their existing immediate transition. No extra checkpoint write is added to normally completed directory pages.

Targeted enumeration, scan-control and transition checks pass. Full final-build offline checks passed for x64 and x86 on 2026-10-17, including the new enumeration cases and the existing interrupted-page, transfer, durability, integrity and encrypted-token checks. Independent review found no new Critical or Important checkpoint-persistence issue. These tests use synthetic HTTP handlers and files; they do not simulate every live Yandex failure or establish an atomic cloud snapshot. The existing behavior for concurrently changed metadata on already discovered duplicate paths is outside this fix.
