# Destination conflicts — 2026-10-18

Task 4 of the Windows stabilization plan. The change prevents a newly listed cloud name from claiming an existing local path or another file's download state.

## Behavior

- A case-insensitive path tree reserves final filenames, their directory ancestors, and download-state paths. File/directory collisions such as `disk:/A` versus `disk:/a/child.txt` fail the affected entries before local verification, archive repair, or download. Unrelated entries continue.
- Cloud files and folders that intersect `.ydr-download`, `.ydr-map`, `.ydr-part`, checkpoint paths, or `.ydr-segments` fail both affected entries. A physical directory where a state file is needed is also rejected.
- Existing assignments take precedence over new cloud entries. Case variants continue to use the established filename and the same eight-character hash suffix as previous releases when a new, unused alternate path is available.
- If existing local bytes have no unambiguous saved owner, the conflicting entries are stopped and the bytes remain in place. No automatic directory renaming or migration of ambiguous data is attempted.
- A removed cloud entry's retained local file and partial state stay reserved. Conflict failures do not overwrite historical verification or resume records.
- The offline catalog applies the same assignment checks, so it cannot advertise a conflicted state file as a verified download.

## Inventory compatibility

Inventory version 3 gains an optional `DestinationAssignments` dictionary. Its keys are case-sensitive cloud paths; its values are validated final paths. The dictionary is saved independently of the current scan list so that an empty or partially refreshed checkpoint retains previous assignments. All cached paths must match the selected root's normalized path or its supported hash suffix.

Complete legacy inventories supply their existing `Destination` values. Incomplete legacy inventories contain provisional paths and cannot prove ownership; ambiguous existing data is retained and reported as a conflict. Unknown cached paths outside the selected root are ignored. The csproj already includes `src/*.cs`.

Older builds can ignore the additional JSON field, but do not implement the new path-stability guarantees. Historical assignments are retained for safety, which adds inventory storage overhead; large-catalog storage redesign remains a separate planned task.

## Verification

Before implementation, focused tests failed on all three reproduced defects: file/directory conflicts entered the pipeline, a refresh reassigned the existing lowercase file, and an ambiguous partial file was assigned to one cloud entry.

Focused checks subsequently passed for:

- File/directory conflicts, cloud directories at reserved state paths, physical directories at map paths, and continued download of unrelated files.
- Adding an uppercase name beside an already downloaded lowercase name, repeated refresh with reversed enumeration order, and preservation of an existing range map with confirmed partial bytes.
- Resuming a simulated interrupted scan whose current file list contains only the new entry while the assignment dictionary retains the previous entry.
- Reading complete legacy inventories, rejecting an outside-root cached assignment, and protecting retained state after its cloud entry disappears.
- Existing `SafeDownloadTests`, including preservation and recovery. Its service-suffix case now expects two visible failures instead of one locally verified file: the filename alone cannot establish whether those bytes belong to a cloud file or a partial download. The original assertions that no download occurs and the bytes remain unchanged are retained.

Full x64 and x86 suites passed with exit code 0 in the ordinary Windows session. Results are recorded in `destinations-2026-10-18/`. These checks use temporary local data and mock API responses; they do not test live account renames or every filesystem configuration.
