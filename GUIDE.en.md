# Yandex Disk Rescue 1.0.4 — guide

[Home](README.en.md) · [Русский](GUIDE.md) · [Releases](https://github.com/fefenke/Yandex-Disk-Rescue/releases)

## 1. Scope and requirements

Only the regular Files section of Yandex Disk is supported. Unlimited photo/video auto-upload is neither counted nor downloaded. The app uses Yandex Disk API technology or the official desktop client, retries available files and records results. It does not repair cloud originals, recover deleted files or guarantee access to unavailable data.

Requires Windows 10 1709+ or Windows 11 and .NET Framework 4.7.1+. Choose x64 for 64-bit Windows, or x86 for 32-bit Windows; x86 also works on x64. There is no separate ARM64 build.

## 2. First launch

1. Download the architecture-specific ZIP and extract the entire archive to a permanent writable folder.
2. Run Start Yandex Disk Rescue.cmd to check prerequisites and launch the app. Settings opens first.
3. Choose API or the official desktop client, fill required fields, select a pass mode and press Start pass at the top.
4. The guide button is always at the top. Transfer settings are locked during passes and automatic retries. Press Stop and wait before changing them.

The data folder beside the executable holds settings, history, inventory, reports and an encrypted token. Each extracted app copy has its own history; a new copy does not automatically use another copy's data.

## 3. API mode

The desktop client is unnecessary. Follow the OAuth link in Settings, sign into the intended Yandex account, obtain a token and enter it. Tokens grant account access: never send them to others or include them in bug reports.

Choose an ordinary local destination, such as an HDD backup folder. Cloud placeholders, links and reparse points are rejected. Cloud folder structure is retained. disk:/ means the regular Disk section; disk:/Photos selects a folder. Different accounts and cloud roots use separate history contexts.

The usual choice is Retry all files not downloaded with the saved list. A missing inventory is fetched automatically.

## 4. Official desktop client mode

Install the official client, sign in and wait for the file list to synchronize. Select its local Disk folder. This app does not request your OAuth token in this mode.

The app requests file retrieval and monitors results; Windows and the client control network transfers. The optional restart-on-errors setting can restart the client. Link concurrency, HTTP partial resume and 32 MiB / 15-second checkpoints apply to API mode only.

## 5. Inventory and pass selection

These are separate settings. The saved inventory avoids enumeration before every attempt. Missing lists are scanned and interrupted scans resume from their saved position. Refresh after cloud files are added, changed or deleted. Refreshing does not force suitable completed files to download again.

Before resuming a started file, the app queries that file's fresh size, modification time and available MD5. This is an individual metadata request, not a full Disk rescan. Failed checks preserve parts and defer the attempt. Changed versions cannot mix old and new data. Refreshed inventory is saved after active jobs finish: on normal completion, manual Stop or handled errors.

Scan errors appear in the log and warning. Finishing enumeration does not prove every cloud folder was accessible. Counts describe discovered files; errors prevent a completeness guarantee. A sudden crash may leave an older intact inventory, and the started file's version is checked again when resuming.

Pass modes:

- Resume unfinished pass: unprocessed files and user-cancelled attempts, rather than every completed failure.
- Retry server/sync errors: recorded errors of that type.
- Retry timeouts and failures: interrupted, stalled and otherwise unsuccessful transfers.
- Retry all files not downloaded: everything lacking a suitable completed local copy; the usual retry choice.
- Start pass from the beginning: check the entire list again. Suitable local copies are not downloaded again.
- Resume partial downloads · API: files with saved bytes in .ydr-part or .ydr-segments. Automatic repeats retain this queue.

## 6. Timeouts and retry queue

Link timeout measures the API response after request dispatch. Default 3 seconds, range 1–60. Queueing, API launch spacing and shared cooldown do not consume it.

File idle timeout measures the download response and further bytes. Default 3 seconds, range 1–60, separately per stream. It resets on response headers and new data. It is not a total-duration limit: a file can download for hours while bytes continue arriving. Saved-data checks run before requests, outside network idle timeout. In client mode this setting measures lack of retrieval progress.

Up to two extra attempts follow new bytes and a network interruption, idle timeout or incomplete response. Retries use the same link and saved offset, rejoining the tail of the shared connection queue. Both retries use the selected idle timeout +2 seconds: 3 → 5 → 5. Even small new progress may permit the next retry within this cap.

An attempt with no new bytes ends quick retries; the file remains for the next matching pass. Old saved parts are not new progress. HTTP errors, invalid ranges, MD5 failures, disk write errors and Stop do not trigger quick retries.

A working signed link is retained only in memory within one automatic-pass session after receiving data. No arbitrary lifetime is assumed. Rejection or zero new bytes causes a fresh link request on the next pass, without an immediate refresh chain in the failed attempt. Restarting the app or starting a new manual session creates a new link cache.

## 7. Continuous passes and API throttling

Continuous passes are enabled by default. Normal completion starts preparing the next problem pass without a mandatory 30-second pause. Preparation is not instantaneous: reports, history and local checks still take time. Partial-download mode keeps its own queue.

Temporary whole-pass failures back off for 1, 2, 4, 8, 16, up to 30 seconds. Normal completion resets this delay. An empty matching queue or a nonrecoverable error ends the session. Stop cancels both the pass and waiting.

All REST API calls share one pacer. HTTP 429 means too many requests, 408 a request timeout, and 5xx a server error. Retry-After is honored as seconds or a date. Without a usable header, cooldown grows through 1, 2, 4, 8, 16, 32, up to 60 seconds. Server instructions may require a longer delay.

The launch interval temporarily rises, up to 5000 ms, and gradually returns to the selected value after healthy responses. Saved settings are unchanged, and automatic passes share the state. The UI shows the reason, countdown and temporary interval. In-flight requests and ready-link downloads continue. Storage-server failures do not throttle the REST API through this pacer. Preparation, API waiting and downloading are separate stages.

## 8. Concurrency and request interval

Defaults are up to 7 link tasks and 20 download connections. Each setting ranges from 1–30 and works independently: new links are prepared while files download. All parts of all files share the 20-connection limit; it does not mean 20 files with four connections each. Queues are bounded rather than creating a task for every cloud file.

API spacing is 25–1000 ms, default 50 ms. Approximate maximum launch rates are 40, 20, 10, 5 and 1 per second at 25, 50, 100, 200 and 1000 ms. This is request frequency, not in-flight count. Section 2.2 of the API terms specifies 40 requests per second: https://yandex.ru/legal/disk_api/ru/ . Error handling may temporarily increase the interval.

More connections do not create bandwidth. Increasing concurrency usually cannot help an already saturated connection. On HDDs, simultaneous writes to separate parts can slow the drive. Try fewer connections and 1–2 streams per file under disk load. Concurrency may help when server waits leave bandwidth idle. Change one setting at a time and compare several passes using equal timeouts, successful files and byte totals.

## 9. Large-file parts

Streams per file: 1–8, default 4 from 256 MB. Threshold: 64–4096 MB; MB means 1,000,000 bytes. New splitting requires cloud MD5. Small files use one stream. Setting 1 disables new splitting; existing parts retain their plan and resume with the selected concurrency.

The first range request checks server support. Valid headers allow the other parts to begin. Ignored or rejected ranges fall back to a full response. Invalid ranges and MD5 failures never count as success. One stalled part need not stop the others.

The destination's .ydr-part and .ydr-segments folder hold a plan and separate parts. Completed parts are merged, size and available MD5 are checked, and the temporary file becomes final. Interrupted merging rolls back and reuses completed parts. In addition to parts, merging needs space for the assembled copy, up to the file's full size. Direct range writes into one shared file are not implemented in this version.

## 10. Checkpoints, HDDs and recovery

Each active stream checkpoints after 32 MiB of new data OR 15 seconds when the next data arrives, whichever occurs first. Both counters reset after saving. This is not a 15-second pause or network timeout. If only 5 MiB arrived in 15 seconds, those bytes are saved; another 27 MiB afterwards does not reach the next 32 MiB threshold.

Normal Stop, interruptions and attempt completion also checkpoint remaining bytes. Data is flushed to disk before the SHA-256 extent journal in .ydr-state is committed. Recovery verifies confirmed extents, discards corrupt or unconfirmed tails and redownloads them. Up to 32 MiB can remain unconfirmed per active stream, rather than per whole file or session.

Keep .ydr-state, .ydr-part and .ydr-segments until completion. Legacy parts without journals may be reused when cloud MD5 is known, subject to final verification. Cloud version changes invalidate incompatible data.

Saving briefly delays the corresponding worker; other downloads can continue but share the drive. One SATA HDD/NTFS test wrote 2 GiB in 77.9 seconds without checkpoints and 84.9 seconds with SHA-256/checkpoints (+7.0 seconds, about 9%). This is one synthetic measurement, not a guarantee for other files or drives. Recovery reads saved bytes; a real reboot has a cold system cache, so warm-memory reads do not predict its duration.

Tests killed a separate process during downloading and merging, corrupted saved data and journal records, and verified final MD5. Physical power removal was not performed. Durability depends on a healthy drive/controller honoring flush commands; absolute protection against hardware failure or ignored flush requests cannot be promised.

## 11. What file verification proves

When cloud MD5 is known, size and digest are checked. For a previously verified file, history, cloud identity, length and local write timestamp can avoid a full reread. Start from the beginning performs full checks and may take time on large files. The UI shows filename, checked counts and hashing progress; completed counts remain provisional during verification.

Without cloud MD5, matching size currently suffices. This is weaker and does not prove identical contents. Local SHA-256 protects saved extents but cannot replace the cloud original's missing digest. A separate strict status for that case is not implemented in 1.0.4.

An unsuitable existing file remains while a new temporary copy downloads, but may be replaced after the new copy passes checks. Never-overwrite mode and automatic archives of old copies are not implemented. Keep your own backup before using a destination containing unique manual edits.

## 12. Interface, history and errors

Overview shows categories present in the list. Files and folders shows folder progress including descendants and the selected folder's contents. Search and remaining-only filtering apply to that folder. Double-click opens completed files or folders; context menus offer Open and Show in Explorer. Partial files cannot open as completed files.

History and inventory are local; a large initial history loads in the background. Downloaded total counts completed files, while this pass counts new successes. Mode changes the selected queue; remaining refers to the whole list. The log has search and filters; pass history and detailed reports live in data.

A link timeout means downloading has not started. A data timeout means no response or more bytes, not automatically HTTP 500. Check tokens for 401 and access for 403. HTTP 429 triggers API cooldown; 500/503 are server failures. IOException may describe network or disk trouble: inspect the stage, code and log. Retries cannot guarantee the cloud original will become available.

## 13. Updates and privacy

Automatic update checks run at startup and every 6 hours. They can be disabled; manual checks remain available. Installation is offered while idle and requires confirmation. The architecture-specific ZIP is checked against GitHub SHA-256. The app exits, replaces program files, retains data and restarts; failed replacement rolls back.

For manual updates, close the app first. Preserve data and download parts, then replace application files from the whole ZIP. Do not overwrite the history of a running copy or import someone else's data. Windows DPAPI encrypts the saved token for your Windows account; requests use the token in memory.

Never publish data, api-token.bin, personal-path logs or signed URLs. Remove tokens, signed query parameters and personal filenames from bug reports. Revoke leaked access through Yandex ID. Public sources and release archives must contain no user data.

Independent unofficial project. [Report an issue](https://github.com/fefenke/Yandex-Disk-Rescue/issues) · [MIT license](LICENSE).
