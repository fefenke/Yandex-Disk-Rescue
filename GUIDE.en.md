# Yandex Disk Rescue 1.1.2 — guide

## 1. Scope and requirements

Only the regular Files section of Yandex Disk is supported. Prepare ordinary folders first for media stored only in unlimited auto-upload; see below. The app uses Yandex Disk API technology, retries available files and records results. It does not repair cloud originals, recover deleted files or guarantee access to unavailable data.

Requires Windows 10 1709+ or Windows 11 and .NET Framework 4.7.1+. Choose x64 for 64-bit Windows, or x86 for 32-bit Windows; x86 also works on x64. There is no separate ARM64 build.

## 2. First launch

1. Download the architecture-specific ZIP and extract the entire archive to a permanent writable folder.
2. Run YandexDiskRescue.exe. Start Yandex Disk Rescue.cmd is an optional launcher that checks Windows/.NET and explains incompatible environments. Settings opens first.
3. Enter the OAuth token and destination, select a pass mode and press Start pass at the top.
4. The guide button is always at the top. Transfer settings are locked during passes and automatic retries. Press Stop and wait before changing them.

The data folder beside the executable holds settings, history, inventory, reports and an encrypted token. If settings.json is damaged, the app tries settings.json.bak and displays a notice. A settings-save error is displayed separately and prevents starting with unsaved parameters; check write permissions and free space. Each extracted app copy has its own history; a new copy does not automatically use another copy's data.

## 3. API mode

**API downloads do not require a Yandex 360 subscription.** A Yandex account and OAuth token are sufficient.

The desktop client is unnecessary. Follow the OAuth link in Settings, sign into the intended Yandex account, obtain a token and enter it. Tokens grant account access: never send them to others or include them in bug reports.

Choose an ordinary local destination, such as an HDD backup folder. Cloud placeholders, links and reparse points are rejected. Cloud folder structure is retained. disk:/ means the regular Disk section; disk:/Photos selects a folder. Different accounts and cloud roots use separate history contexts.

The usual choice is Download all remaining files with cloud refresh unchecked. A missing inventory is fetched automatically.

## 4. Fast starts and bounded history

Unchanged completed files use saved cloud-version, size and local-write-time evidence. Check all files performs full content verification again.

The history.latest index contains latest records. A missing, corrupt or stale index falls back to history.jsonl. After a successful pass, a journal of at least 16 MiB is compacted when replacement would halve its size. Every file retains its latest state and resume/verification evidence. Separate failed-cloud-version records remain intact; history cleanup never enables automatic retries of those versions.

The pass list retains 1,000 entries; each download context retains 50 detailed reports. Current, busy, unknown and unverified reports are preserved. Journal replacement uses a flushed temporary file and atomic replacement; failure leaves the original intact. User cancellation skips large-journal compaction.

Moving the app together with data retains map identity through the account/folder context rather than installation location. Compatible old maps migrate while rechecking confirmed extents. Preparation can recover compatible extents from the app-owned former-files archive; nonempty originals remain. Only proven empty service placeholders are removed. Foreign, corrupt and ambiguous data is not accepted automatically.

Missing or invalid API sizes are not zero. Old unconfirmed zero sizes are refreshed before accepting completion; genuine empty files remain supported.

## 5. Inventory and pass selection

Cloud-list refresh and local-file processing are independent settings.

- Refresh cloud file list when starting unchecked: use the saved inventory. A missing inventory is scanned automatically; interrupted scans resume by rereading the unfinished folder.
- Parent checked: refresh on each manual Start. Automatic repeats reuse that list.
- Also refresh before every automatic pass is available with the parent checked. It refreshes each automatic repeat, adding enumeration time and API requests. Stop ends the session; the next Start is a new first pass.

Refreshing finds new and changed cloud files without forcing all completed local files to be rehashed. To verify current data on both sides, enable refresh and choose Check all files.

Before resuming a started file, its current cloud version is still requested individually, even with both options unchecked. Failed checks preserve saved parts; different versions are not mixed.

Pass modes:

- Check all files: verify local size and available cloud hash, then download missing or replacement files. Suitable local copies are not downloaded again. Automatic repeats switch to remaining files rather than rehashing every completed file again.
- Download all remaining files: missing, partial and failed files; the usual continuation mode.
- Download files with download errors: recorded timeouts, network and server errors. Only errors for the current cloud version qualify. New files, changed versions and user-cancelled attempts stay excluded, including after refresh. Use Download all remaining files for those files.
- Resume partial files: only files with saved parts. Never starts new downloads, including after automatic cloud refresh.

- Download files with checksum mismatches: one pass per manual Start. You may start another manual pass for another attempt. This mode never starts the next automatic pass; ordinary bounded network retries within a job remain available.

Ordinary modes and cloud refresh do not unblock a failed cloud version. Refresh keeps the selected mode; Check all files alone narrows to remaining files on automatic repeats. Automatic repeats stop when no eligible files remain; this is not a continuous cloud-watching mode.

## 6. Timeouts and retry queue

Link timeout measures the API response after request dispatch. Default 3 seconds, range 1–60. Queueing, API launch spacing and shared cooldown do not consume it.

File idle timeout measures the download response and further bytes. Default 3 seconds, range 1–60, separately per stream. It resets on response headers and new data. It is not a total-duration limit: a file can download for hours while bytes continue arriving. Saved-data checks run before requests, outside network idle timeout.

Up to two extra attempts follow new bytes and a network interruption, idle timeout or incomplete response. Retries use the same link and saved offset, rejoining the tail of the shared connection queue. Both retries use the selected idle timeout +2 seconds: 3 → 5 → 5. Even small new progress may permit the next retry within this cap.

An attempt with no new bytes ends quick retries; the file remains for the next matching pass. Old saved parts are not new progress. HTTP errors, invalid ranges, MD5 failures, disk write errors and Stop do not trigger quick retries.

A working signed link is retained only in memory within one automatic-pass session after receiving data. No arbitrary lifetime is assumed. Download-server HTTP 401, 403 or 410 triggers one shared refresh after this file's readers finish and fresh metadata confirms the same cloud version. Remaining ranges resume with the new link; completed ranges are retained. Changed versions, failed checks or refreshes, and another rejected link leave the file for the next matching pass. HTTP 404 and ordinary timeouts do not trigger this refresh. Restarting the app or starting a new manual session creates a new link cache.

## 7. Continuous passes and API throttling

Continuous passes are enabled by default. After a completed pass, the app prepares another attempt for the remaining problem files. Preparation includes saving results and checking local files. Partial-download mode keeps its own queue.

After a temporary whole-pass failure, the app waits before retrying to avoid overloading the server. An empty matching queue or a nonrecoverable error ends the session. Stop cancels both the pass and waiting.

All REST API calls share one pacer. HTTP 429 means too many requests, 408 a request timeout, and 5xx a server error. Retry-After is honored as seconds or a date. Without a usable header, cooldown grows through 1, 2, 4, 8, 16, 32, up to 60 seconds. Server instructions may require a longer delay.

The launch interval temporarily rises, up to 5000 ms, and gradually returns to the selected value after healthy responses. Saved settings are unchanged, and automatic passes share the state. The UI shows the reason, countdown and temporary interval. In-flight requests and ready-link downloads continue. Storage-server failures do not throttle the REST API through this pacer. Preparation, API waiting and downloading are separate stages.

## 8. Concurrency and request interval

Defaults are up to 7 link tasks and 20 download connections. Each setting ranges from 1–30 and works independently: new links are prepared while files download. All parts of all files share the 20-connection limit; it does not mean 20 files with four connections each. Queues are bounded rather than creating a task for every cloud file.

API spacing is 25–1000 ms, default 50 ms. Approximate maximum launch rates are 40, 20, 10, 5 and 1 per second at 25, 50, 100, 200 and 1000 ms. This is request frequency, not in-flight count. Section 2.2 of the API terms specifies 40 requests per second: https://yandex.ru/legal/disk_api/ru/ . Error handling may temporarily increase the interval.

More connections do not create bandwidth. Increasing concurrency usually cannot help an already saturated connection. On HDDs, simultaneous writes to separate parts can slow the drive. Try fewer connections and 1–2 streams per file under disk load. Concurrency may help when server waits leave bandwidth idle. Change one setting at a time and compare several passes using equal timeouts, successful files and byte totals.

## 9. Large-file parts

Streams per file: 1–8, default 4 from 256 MB. Threshold: 64–4096 MB; MB means 1,000,000 bytes. New splitting requires cloud MD5 or SHA-256. Small files use one stream. Setting 1 disables new splitting; existing parts retain their plan and resume with the selected concurrency.

The first range request checks server support. Valid headers allow the other parts to begin. Ignored or rejected ranges fall back to a full response. Invalid ranges and MD5 failures never count as success. One stalled part need not stop the others.

All streams write their ranges directly into one .ydr-download payload. The .ydr-map journal records confirmed extents and SHA-256 hashes. NTFS needs no final merge or second full assembled copy. Allocated payload length is not progress: the map determines downloaded bytes. Drives without sparse-file support use the legacy format.

## 10. Checkpoints, HDDs and recovery

Each active stream checkpoints after 32 MiB of new data OR 15 seconds when the next data arrives, whichever occurs first. Both counters reset after saving. This is not a 15-second pause or network timeout. If only 5 MiB arrived in 15 seconds, those bytes are saved; another 27 MiB afterwards does not reach the next 32 MiB threshold.

Normal Stop, interruptions and attempt completion checkpoint pending bytes. Payload data is flushed before the .ydr-map record. Recovery verifies saved extents and redownloads corrupt or unconfirmed bytes. Up to 32 MiB may remain unconfirmed per active stream. Local writes and flushes do not consume the network idle timeout.

Keep .ydr-download and .ydr-map until completion. Legacy .ydr-part/.ydr-state/.ydr-segments data migrates automatically; sources are deleted only after validating the new map. Incompatible old versions are preserved in the former-files archive.

Saving briefly delays the corresponding worker; other downloads can continue but share the drive. In the legacy 1.0.4 format, one SATA HDD/NTFS test wrote 2 GiB in 77.9 seconds without checkpoints and 84.9 seconds with SHA-256/checkpoints (+7.0 seconds, about 9%). This is one synthetic measurement, not a guarantee for other files or drives. Recovery reads saved bytes; a real reboot has a cold system cache, so warm-memory reads do not predict its duration.

Tests killed a separate process during downloading and merging, corrupted saved data and journal records, and verified final MD5. Physical power removal was not performed. Durability depends on a healthy drive/controller honoring flush commands; absolute protection against hardware failure or ignored flush requests cannot be promised.

## 11. What file verification proves

If relocation gave this context's former-copy archive a `(YDR-…)` suffix, the ordinary name is restored when available or occupied only by a service marker. Files and history references are retained; an interrupted rename resumes before a later pass. Directories containing other files or conflicting with cloud destinations are not merged automatically.

Size is checked first, followed by cloud MD5, or SHA-256 only when MD5 is absent. SHA-256 never overrides an MD5 mismatch. Ordinary passes may reuse history for unchanged versions, size and local write time, marked as previously checked. Start from the beginning reads and hashes the complete file again.

Without either cloud hash, downloads remain allowed and are marked as size-only evidence. Local map SHA-256 protects saved extents but cannot prove equality with the cloud original.

The existing file stays while a new temporary copy downloads and is checked. Before replacement, a wrong-size file moves to the former-files archive; a hash-mismatched file moves to the verification-error archive. Subfolders and unique names are retained. A new bad copy is also preserved in the error archive. Failure to preserve the old file prevents replacement. Verification of large copies can be stopped before the move intent is recorded. Once that intent is durable, the app first completes the recoverable operation; Stop can wait for that operation to finish.

## 12. Interface, history and errors

Overview shows categories present in the list. Files and folders shows folder progress including descendants and the selected folder's contents. Search and remaining-only filtering apply to that folder. Double-click opens completed files or folders; context menus offer Open and Show in Explorer. Partial files cannot open as completed files.

History and inventory are local; a large initial history loads in the background. Downloaded total counts completed files, while this pass counts new successes. Mode changes the selected queue; remaining refers to the whole list. The log has search and filters; pass history and detailed reports live in data.

A link timeout means downloading has not started. A data timeout means no response or more bytes, not automatically HTTP 500. Check tokens for 401 and access for 403. HTTP 429 triggers API cooldown; 500/503 are server failures. IOException may describe network or disk trouble: inspect the stage, code and log. Retries cannot guarantee the cloud original will become available.

## 13. Updates and privacy

Automatic update checks run at startup and every 6 hours. They can be disabled; manual checks remain available. Prereleases are excluded. Update downloads can be cancelled; read idle timeout is 30 seconds and total download timeout is 15 minutes. Installation is offered while idle and requires confirmation. The architecture-specific ZIP is checked against GitHub SHA-256. The app exits, replaces program files, retains data and restarts; failed replacement rolls back.

For manual updates, close the app first. Preserve data and download parts, then replace application files from the whole ZIP. Do not overwrite the history of a running copy or import someone else's data. Windows DPAPI encrypts the saved token for your Windows account; requests use the token in memory.

Never publish data, api-token.bin, personal-path logs or signed URLs. Remove tokens, signed query parameters and personal filenames from bug reports. Revoke leaked access through Yandex ID. Public sources and release archives must contain no user data.

Independent unofficial project. [Report an issue](https://github.com/fefenke/Yandex-Disk-Rescue/issues) · [MIT license](LICENSE).


A failed cloud version is blocked from automatic retries, including rescanning and full passes. Deleting its preserved artifact keeps it blocked and displays copy missing. Download files with checksum mismatches in the pass mode selector runs one explicit pass. Identical preserved bad copies are deduplicated. Changed cloud versions may download again; old artifacts remain. Keep data to retain these version records.


Local destination assignments survive cloud refreshes, including case-only name differences. Collisions with another file, a directory, a service artifact or ambiguous existing data fail the affected downloads. Saved bytes remain in place; unrelated downloads continue. Do not manually rename parts or maps to bypass a conflict.


Check and install updates in Settings; there is no separate sidebar update button. You can update directly to the latest version while retaining data, without installing intermediate releases. Old history records tolerate missing fields; files are checked again when the evidence is insufficient for a fast check.

After updating, the app removes proven-empty service placeholders and empty subfolders from its own archive once. Nonempty files, uncertain objects, the archive root and .ydr-owner remain. Stop or access errors allow cleanup to continue later.

## Unlimited-upload photos and privacy

The Photos view and ordinary folders can contain the same media. Independently downloading both without reconciliation can duplicate data and traffic. This project uses ordinary folders as one inventory source. On the official Yandex Disk website, copy only the photos missing from ordinary folders into a chosen folder, then refresh the app inventory. Retain originals until the backup is checked. Copies from unlimited storage count against your storage quota: see [Yandex help](https://yandex.ru/support/yandex-360/customers/disk/web/ru/media-upload.html?lang=ru).

Additional schemes involving third-party sites, session cookies or public links would expand access to private data. This does not mean every possible unlimited-storage implementation is inherently unsafe. Public links are unnecessary here: anyone receiving an accessible link may read, download and retain its contents. Avoid untrusted recovery sites, revoke unwanted public sharing, and remember that revocation cannot delete copies already obtained by others.

## Interrupted scans and cloud changes

An unreadable folder keeps the inventory incomplete and its path is saved for retry on the next pass. Discovered files can still download. Old inventories with listing errors are rebuilt automatically on the first Start after updating; completed local files and history are retained, and full local hash verification is not required for this repair.

An interrupted folder restarts at its first page; saved paths suppress duplicate entries. Completed folders are retained in saved-list mode. Refresh the entire inventory to find changes there, and perform a fresh-list control pass after the main backup. An offset is a position, not a stable cursor. Concurrent cloud edits can still shift pages; enumeration is not an atomic snapshot or proof of completeness.

## After backup

Source, builds and tests are inspectable. Downloads and history processing run locally, using Yandex API/download hosts and GitHub update checks, without a project-operated file relay. Open source does not authenticate an arbitrary executable: use this repository's release, verify its checksum or build yourself.

Windows DPAPI protects api-token.bin for the current Windows user. The token is used in memory; downloaded files, history and reports are not automatically encrypted. Keep data, tokens, session cookies and signed URLs private. Malware running as the same Windows user remains a risk.

After checking the completed backup, revoke the used application's data access in [Yandex ID security](https://id.yandex.ru/security), then clear the app's local token. Removing a local token does not revoke remote authorization. See [Yandex ID help](https://yandex.ru/support/id/ru/profile).

[Project design](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/DESIGN-DECISIONS.md) · [Tests and limitations](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/TESTING.md).

Downloads use the API exclusively. The Yandex Disk desktop client and Windows Cloud Files are unnecessary. Old Desktop pass records remain historical statistics; they cannot activate client execution and are not imported into API history. Keep the entire data folder when updating.
