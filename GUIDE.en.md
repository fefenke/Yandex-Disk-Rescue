# User guide

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
Repeat problem files starts later problem passes after 30-second pauses. Normal modes switch to problem-file retries; the cloud list is not refreshed on every pass. Partial-file mode keeps its own queue. Stop, an empty matching queue or a nonrecoverable error ends the series.
Idle timeout limits waiting without new bytes, not total download duration. Initially API waiting also includes obtaining the link and first response. New data resets it. Start with 1 second for a quick pass; try 3–5 seconds for repeated timeouts. Short waits can cancel slow but working downloads.
Increase idle wait up to 5 sec is active only with automatic retries. Every two completed nonempty passes add 2 seconds up to 5: 1,1,3,3,5 or 3,3,5. Initial values of 5 or more stay unchanged. Each new session restarts from its initial value. Connection failures, incomplete scans and empty passes do not advance the schedule.
Temporary API connection/listing failures retry independently at the current folder/page: first immediately, then 1/2/up to 5 seconds and server Retry-After. Invalid tokens and access denial require attention.

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
Version 1.0.1 supports in-app updates while preserving user data.

SECURITY AND LIMITATIONS
Windows DPAPI encrypts the saved token for your Windows account. It is decrypted in memory for use; storage encryption does not replace trust in the application. Do not share tokens; revoke unused access in Yandex ID. Never publish your data folder. Preserve it when updating and do not overwrite the history of a running app.
Independent unofficial project, unaffiliated with Yandex. It does not repair server failures, recover deleted data or guarantee every download. Unlimited auto-upload is unsupported.
