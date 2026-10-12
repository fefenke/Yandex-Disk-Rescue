# Yandex Disk Rescue

**Unlimited photo/video auto-upload files are NOT counted or downloaded. Only files in the regular Files section are supported.**

[Русский](README.md) · v0.2.4 · portable Windows 10 1709+ / Windows 11 app · preview release

**Windows 64-bit (x64)** — for most PCs.

[![Download Windows x64](https://img.shields.io/badge/Windows_64_bit-x64_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v0.2.4/YandexDiskRescue-win-x64.zip)

**Windows 32-bit (x86)** — for a 32-bit Windows installation.

[![Download Windows x86](https://img.shields.io/badge/Windows_32_bit-x86_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v0.2.4/YandexDiskRescue-win-x86.zip)

Extract the complete ZIP into a permanent folder and run **Start Yandex Disk Rescue.cmd**. Requires .NET Framework 4.7.1 or newer. Do not launch directly from the ZIP.

## What's new in v0.2.4

- Green **Downloaded total** card before **Downloaded this pass**.
- Default: API, cached inventory, retry all files not downloaded, automatic retries.
- Temporary API/network failures retry after 30 seconds; an empty initial pass still proceeds to existing problem files.
- Zero-byte and partial downloads grouped under timeout/download failures.
- Separate detailed guide button and simpler pass modes.
- Persistent pass statistics.

## Setup

**API:** obtain an OAuth token from the [Yandex Disk playground](https://yandex.ru/dev/disk/poligon/), enter it and choose a download folder. `disk:/` selects the whole Disk; `disk:/Photos` selects a folder. No desktop client or restarts are required. API can be faster, depending on service and network availability.

**Desktop:** install [Yandex Disk](https://disk.yandex.ru/download), sign in, wait for synchronization and select the local Disk folder. The client may restart on synchronization errors.

Open **Open guide** in the app for detailed instructions and combinations.

## Inventory and modes

The upper API selector chooses cached inventory or cloud refresh. Cached inventory is created once if missing; interrupted scans resume. Refresh when cloud files are added or changed. Automatic retries do not refresh inventory on every pass.

| Mode | Files processed |
| --- | --- |
| Resume unfinished pass | Unchecked files and user-interrupted attempts |
| Retry server/sync errors | Recorded server or synchronization errors |
| Retry timeouts and failures | Zero-byte and partial transfers and other failures |
| Retry all files not downloaded | All files not downloaded, including unchecked files |
| Start pass from the beginning | Entire inventory; valid local files are not downloaded again |

Normal use: cached inventory + retry all files not downloaded. After cloud changes: refresh inventory + retry all files not downloaded.

Automatic retries run the selected mode first, then problem files with a 30-second pause. Stop cancels requests and waiting. No remaining problems ends the loop. Invalid tokens, denied access, critical local errors or uncertain cancellation require attention.

## Statistics and private data

Cards show found, downloaded total, downloaded this pass, remaining and errors. Remaining covers all missing files; the pass size depends on its mode. Pass history stores start, duration, mode, outcome and file counts; detailed reports are generated automatically.

History, inventory, reports and the Windows DPAPI-encrypted token stay in local `data` beside the EXE. Preserve this folder when updating. **Never publish data.** Release ZIPs contain no private tokens, history or logs.

Identity uses full path and version; identical names in different folders remain separate. API validates size and available MD5. `.ydr-part` partials are not completed downloads and restart from the beginning on retry. Verified unchanged files use faster reconciliation; full passes verify them again.

Independent community project, not an official Yandex product. Cannot restore deleted files or repair server errors. Unlimited auto-upload is unsupported.

[Support the author](https://yoomoney.ru/fundraise/1KS67440FP0.261012) · [Release notes](https://github.com/fefenke/Yandex-Disk-Rescue/releases/tag/v0.2.4)

## Build

`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Package -Platform x64`

Use `x86` for 32-bit. Local checks: `tests/run-tests.ps1`. License: [MIT](LICENSE).