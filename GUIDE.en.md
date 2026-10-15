# Guide

IMPORTANT: Unlimited photo/video auto-upload files are NOT counted or downloaded. Only the regular Files section is supported.

GETTING STARTED
Extract the entire ZIP to a permanent folder and run YandexDiskRescue.exe. Settings always opens first. Choose API or the official desktop client, fill the required fields and press Start pass. Download settings are locked throughout scanning, downloads and automatic retry pauses. Stop cancels the whole session.

TWO METHODS
API: use the OAuth link, sign into the intended Yandex account, obtain a token and paste it into Settings. Choose an ordinary local destination. disk:/ means the whole Disk; disk:/Photos selects a folder. Cloud folder structure is retained. The desktop client and its restarts are unnecessary, which may improve speed; server availability still determines results.
Desktop client: install the official client, sign in and wait for synchronization to finish. Select its local Disk folder. This app does not request your OAuth token in this mode. Windows and the client manage downloading; this app requests files and monitors results. Sync errors may cause client restarts. HTTP Range resume applies to API only.

API LIST VS PASS MODE
The saved list is used by default without rescanning. Missing lists are fetched and incomplete scans resume. Refresh the list when cloud files are added, changed or deleted. Refreshing the list does not force completed files to download again.

PASS MODES
• Resume unfinished pass: unprocessed files and attempts stopped by the user; previously completed failures are skipped.
• Retry server/sync errors: recorded errors of that type.
• Retry timeouts and failures: interrupted, stalled and other failed downloads, including partial files.
• Retry all files not downloaded: every file lacking a valid completed local copy, including untested files. Recommended for normal use.
• Start pass from the beginning: check the entire list again, including local files. Valid complete copies are not downloaded again.
• Resume partial downloads · API: nonempty .ydr-part files only. Automatic retries stay in this mode; otherwise one pass runs.

AUTOMATIC RETRIES AND TIMING
Continuous passes starts later problem passes after 30-second pauses. Normal modes switch to problem-file retries; the cloud list is not refreshed on every pass. Partial-file mode keeps its own queue. Stop, an empty matching queue or a nonrecoverable error ends the series.
TWO INDEPENDENT API TIMEOUTS
Link timeout, sec: how long to wait for the API download-link response after sending the request. Default: 3 seconds. Queueing and the chosen interval between request launches do not count. A timed-out file remains eligible for another pass.
File idle timeout, sec: wait for the download response, then for new data. Default: 3 seconds. Starts only when downloading begins and resets on response headers and each new block of bytes. Waiting in the ready-link queue does not count. This is not a total download-duration limit. In desktop-client mode it controls time without file-retrieval progress; link timeout is API-only.
Both fields accept 1–60 seconds. With 3 + 3, the API request gets three seconds after sending, then downloading has a fresh three-second idle budget.

CONCURRENT WORK
By default, up to 7 link requests and up to 20 download connections run simultaneously and independently. Links for subsequent files are obtained while other files download. These are ceilings, not guaranteed active-transfer counts. Ready links wait in a bounded queue. The API launch interval defaults to 50 ms.
Defaults: repeat problem files indefinitely, both timeouts fixed at 3 seconds, automatic increases disabled. Stop cancels the whole series. Manual choices persist. When migrating to this pipeline, defaults reset once to 3 + 3 with repeat enabled; history, inventory and token are retained.
Temporary API connection/listing failures retry independently at the current folder/page: first immediately, then 1/2/up to 5 seconds and server Retry-After. Invalid tokens and access denial require attention.

CONCURRENCY: WHAT THE TWO FIELDS CONTROL
Link requests: how many tasks can request download links simultaneously. Default 7; range 1–30. This is in-flight task count, not requests per second. Try increasing it when downloads wait for links; try reducing it if link timeouts, connection failures or HTTP 429 responses increase. More requests do not guarantee more successful downloads.
Download connections: shared active HTTP data request limit for all files and segments combined. Default 20; range 1–30. Queue time does not consume network idle timeout.
7 + 20 is the best balance in our tests and our starting recommendation, not a Yandex limit or a universal optimum. You may choose 30 + 30. The app caps each field at 30 to bound connection and memory use. Queues remain bounded instead of spawning a task for every file.
On a slow connection, try fewer downloads, for example 5–10. Files share your total bandwidth; more transfers do not increase it. If the link is already saturated, increasing concurrency usually adds no overall speed. If server waits leave bandwidth idle, increasing it may help. A few large-file transfers may fill the link, while small files depend more on link acquisition.
Change one setting at a time and compare several passes with equal timeouts: successful files, completed data and elapsed time. A fast pass with more failures is not necessarily better. All transfer settings persist and are locked throughout passes and automatic-retry pauses. Concurrency and link timeout are API-only; Windows and the official desktop client control transfers in client mode.

REQUEST LAUNCH INTERVAL
API launch interval, ms: time between API request launches. Range 25–1000 ms; default 50 ms. Approximate maximum launch rates: 25 ms → 40/s; 50 ms → 20/s; 100 ms → 10/s; 200 ms → 5/s; 1000 ms → 1/s. Requests can wait for responses concurrently without waiting for the preceding response before launching.
Reduce the interval to try a higher launch rate and compare results. Increase it if rate restrictions, HTTP 429 or API instability occur. A lower interval does not guarantee better speed or success. Queueing and the chosen interval do not consume link response time. This setting persists and is locked while running.
Section 2.2 of the official Disk API terms specifies 40 requests per second: https://yandex.ru/legal/disk_api/ru/ . This limits frequency, not the number of tasks waiting concurrently. The default 50 ms leaves headroom below that rate.

RESUME AND INTEGRITY
API resumes nonempty .ydr-part files through HTTP Range when the server accepts the requested range and the cloud version matches. Full responses restart downloading. Size and available MD5 are checked before the temporary file becomes complete. Size-only verification is weaker; damaged cloud originals cannot be repaired.
HTTP 5xx is a server error. IOException can mean a connection, read or write failure and does not prove a server issue. The log records the type/code. A retained partial may exist after a later attempt receives no new bytes.

PRE-PASS VERIFICATION
Before downloading, the app checks history and local files. The header shows checked file counts and overall progress. MD5 checks also show percentage, megabytes read, elapsed time and the current filename. Large videos can take time; elapsed time keeps updating while disk reads are pending. The completed-file count is provisional during this stage. Each application copy keeps its own history beside it; starting another copy with older history may trigger rechecking files already on disk.

INTERFACE
Overview shows only categories present in the list. Files and folders shows folder progress including descendants and the selected folder contents. Search and remaining-only filtering apply to that folder. Tooltips show full names; resize columns/splitters and use horizontal scrolling when needed.
Double-click a completed file to open it or a folder to browse. File menus offer Open and Show in Explorer; partial files cannot open as completed files. Folders offer Open in Explorer. Missing local objects cannot yet be revealed.
Saved inventory/history is read locally without API requests. The first large-history load runs in the background. Counts reflect recorded results, not a new full MD5 verification. Refresh the list to reflect cloud changes.
Downloaded total counts completed files; this pass counts new successes. Remaining covers the whole list while the processed denominator depends on pass mode. The log has search and All events / Successful / Errors filters without automatic scrolling. Pass history stores results; detailed reports live in data.

UPDATES
Check for updates automatically enables startup and 6-hour public GitHub checks. Uncheck it to disable background checks; the setting persists. Manual Check for updates and Update remain available. Installation requires approval while idle.
The architecture-specific ZIP is checked against the GitHub SHA-256 digest. The app exits, replaces program files only and restarts. The data folder with history, inventory, settings and encrypted token is retained. Failed replacement is rolled back. Offline checks do not interrupt downloads. Versions without the updater need one manual update.
Version 1.0.3 supports in-app updates while preserving user data.

SECURITY AND LIMITATIONS
Windows DPAPI encrypts the saved token for your Windows account. It is decrypted in memory for use; storage encryption does not replace trust in the application. Do not share tokens; revoke unused access in Yandex ID. Never publish your data folder. Preserve it when updating and do not overwrite the history of a running app.
Independent unofficial project, unaffiliated with Yandex. It does not repair server failures, recover deleted data or guarantee every download. Unlimited auto-upload is unsupported.


ERROR MESSAGES
Download link request timed out. means the API did not return a link within Link timeout; downloading has not started in that attempt. A file idle timeout means the download response or more data did not arrive in time. A timeout alone does not imply HTTP 500 or file corruption.
HTTP 429 means too many requests: increase the API launch interval and try later. HTTP 503 means temporarily unavailable; HTTP 500 means an internal server error. Retrying may help temporary failures but cannot guarantee availability.
Continuous passes retain the selected timeouts without automatic increases.


MULTI-STREAM LARGE-FILE DOWNLOADS · API
Streams per large file: 1–8, default 4. For files from, MB: size threshold 64–4096 MB, default 256. One MB equals 1,000,000 bytes. New files at or above this threshold with known MD5 are split into byte ranges. Small files use one stream. Set 1 to disable splitting new files. Saved parts retain their layout and resume with the selected concurrency even after the threshold changes.
The API link comes first. The first range request checks server support; after valid response headers, remaining parts enter the shared download queue. All streams count toward Download connections: a limit of 20 never permits 20 files with four active connections each. Actual concurrency may be lower.
The file idle timeout applies separately to each stream, starts after queueing when its request is sent, and resets on response headers and new data. Three seconds limits idle time, not the total duration of a part. A stalled stream stops while other parts continue. Saved parts retry on later passes; without continuous passes, start the next pass manually.
Parts are stored beside the destination in its .ydr-segments directory. Keep it and .ydr-part until completion. Completed parts are reused and incomplete parts resume at their saved offsets. A changed cloud version discards old parts. Interrupted merging rolls back on the next pass and reuses the saved parts.
Size and MD5 are checked before promoting the assembled file and removing parts. If Range is ignored or rejected, downloading falls back to a single response. Invalid ranges and MD5 mismatches are never treated as success.
More streams may help large files but cannot fix unavailable originals or link errors. On slow connections try 1–2 streams or increase file idle timeout if active downloads keep timing out. Merging temporarily needs extra disk space up to the size being assembled, in addition to saved parts. Settings persist and lock while running. This feature applies to API mode.
