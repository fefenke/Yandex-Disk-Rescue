<div align="center">
<p><strong>If the app helped save your files, you can support the author 💜</strong><br>Optional, always. The app remains free.</p>
<a href="https://yoomoney.ru/fundraise/1KS67440FP0.261012"><img alt="Support_the_author" src="https://img.shields.io/badge/Support_the_author-ЮMoney-7556D9?style=for-the-badge"></a>
</div>

![Yandex Disk Rescue](https://raw.githubusercontent.com/fefenke/Yandex-Disk-Rescue/main/assets/readme-cover.svg)

<div align="center">

[![Build](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml/badge.svg)](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml)
[![1.0.2 release](https://img.shields.io/badge/1.0.2-release-5965E9)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/tag/v1.0.2)
[![MIT](https://img.shields.io/badge/license-MIT-208B74)](LICENSE)

[Русский](README.md) · **English** · [Full guide](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/GUIDE.en.md)

</div>

Free Windows app for downloading available Yandex Disk files, keeping history and retrying failures. Use the API with a token or the official desktop client without entering a token into this app.

> [!IMPORTANT]
> **Unlimited photo/video auto-upload files are NOT counted or downloaded.** Only the regular **Files** section is supported. The app does not repair server failures or guarantee every download.

## Download

[![Windows x64](https://img.shields.io/badge/Windows_64_bit-x64_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.0.2/YandexDiskRescue-win-x64.zip)
[![Windows x86](https://img.shields.io/badge/Windows_32_bit-x86_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.0.2/YandexDiskRescue-win-x86.zip)

Windows **10 1709+ / 11** · .NET Framework **4.7.1+** · RU / EN · portable.

**Version 1.0.2.** Extract the complete ZIP to a permanent folder and run **Start Yandex Disk Rescue.cmd**.

![Folder progress and download states](assets/preview-api-en.png)

*Synthetic demonstration data, no personal files.*

| Download | Understand progress | Resume work |
| --- | --- | --- |
| Five concurrent downloads, retries and API resume | Folder tree, categories, log and pass history | Cached inventory, history, completed-file checks and application updates |

## Quick start

The app opens **Settings**. **Guide** and **Start pass** stay visible at the top.

| API · default | Official desktop client |
| --- | --- |
| Follow the OAuth link in Settings, obtain a token and paste it into the app | Install the client, sign in and wait for synchronization |
| Select an ordinary destination; `disk:/` means the whole Disk | Select the client's local Yandex Disk folder |
| No client needed; Windows DPAPI protects the stored token | No token entered into this app; the client manages downloads |

Normally use the **saved list** and **Retry all files not downloaded**. The list is created on first use; refresh it after cloud changes rather than on every retry. Download settings are locked while working.

<details>
<summary><strong>Passes, timeouts and resume</strong></summary>

- Resume unfinished: unprocessed files and cancelled attempts.
- Server/sync errors or timeouts/failures: retry the selected group.
- All not downloaded: everything lacking a valid completed local copy.
- From the beginning: recheck the entire list; completed files are not downloaded again.
- Resume partial downloads · API: nonempty `.ydr-part` files only.

Automatic retries run problem passes after 30-second pauses without rescanning. Partial mode keeps its partial-only queue. API has two independent fields: **Link timeout** starts after sending the request; **File idle timeout** waits for the download response and new bytes. Defaults are **3 + 3 seconds**, repeat enabled, gradual increases disabled. Queueing and the selected launch interval do not consume the timeout budgets. Up to **7 link requests and 20 downloads run concurrently**. Optional increases affect only file idle time, for example **3,3,5** seconds; a new session resets the sequence.

API resume uses HTTP Range if supported and the cloud version matches. Size and available MD5 are checked before completion. HTTP 5xx, timeouts and IOException are different causes; IOException alone does not prove a server error.

</details>

<details>
<summary><strong>Interface and updates</strong></summary>

Pre-pass verification shows checked file counts; MD5 checks show percentage, megabytes and elapsed time. Large files can take a while, and the completed-file count is provisional during verification.

Double-click a completed file to open it or a folder to browse. Context menus offer Open and Show in Explorer. Tooltips show full names; columns and panels resize. The log keeps selection/scroll position and offers All events / Successful / Errors filters. Overview hides absent categories.

Disable automatic update checks in Settings if preferred; manual Check for updates and Update remain available. Automatic checks run on startup and every 6 hours. Installation requires approval while idle. The architecture-specific ZIP is verified against GitHub's SHA-256 digest. Only program files are replaced; `data` retains history, inventory, settings and the encrypted token. Close the app and preserve `data` for manual updates too.

</details>

## Security and help

The token is stored locally using **Windows DPAPI**, bound to your Windows account, and decrypted in memory when needed. Storage encryption does not replace trust in the code. Never publish your `data` folder or share tokens; revoke access when no longer needed.

[Full guide](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/GUIDE.en.md) · [Report a bug](https://github.com/fefenke/Yandex-Disk-Rescue/issues) · [Release notes](https://github.com/fefenke/Yandex-Disk-Rescue/releases/tag/v1.0.2)

Include the app version, download method and error text. Exclude tokens, personal paths and your full history.

<details>
<summary>Build from source</summary>

```powershell
.\build.ps1 -Package -Platform x64
.\build.ps1 -Package -Platform x86
```

Windows and .NET Framework 4.7.1+ required. Run `YandexDiskRescue.exe --self-test` for checks. [CI builds](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml).

</details>

Independent unofficial project, unaffiliated with and not endorsed by Yandex. Service names belong to their respective owners. [MIT license](LICENSE).

<details>
<summary>Link and file timeouts · API</summary>

TWO INDEPENDENT API TIMEOUTS
Link timeout, sec: how long to wait for the API download-link response after sending the request. Default: 3 seconds. Queueing and the chosen interval between request launches do not count. A timed-out file remains eligible for another pass.
File idle timeout, sec: wait for the download response, then for new data. Default: 3 seconds. Starts only when downloading begins and resets on response headers and each new block of bytes. Waiting in the ready-link queue does not count. This is not a total download-duration limit. In desktop-client mode it controls time without file-retrieval progress; link timeout is API-only.
Both fields accept 1–60 seconds. With 3 + 3, the API request gets three seconds after sending, then downloading has a fresh three-second idle budget.

CONCURRENT WORK
Up to 7 link requests and up to 20 downloads run simultaneously and independently. Links for subsequent files are obtained while other files download. These are ceilings, not guaranteed active-transfer counts. Ready links wait in a bounded queue. The API launch interval defaults to 50 ms.
Defaults: repeat problem files indefinitely, both timeouts fixed at 3 seconds, automatic increases disabled. Stop cancels the whole series. Manual choices persist. When migrating to this pipeline, defaults reset once to 3 + 3 with continuous passes enabled; history, inventory and token are retained.

</details>

<details>
<summary>Concurrency and tuning</summary>

CONCURRENCY: WHAT THE TWO FIELDS CONTROL
Link requests: how many tasks can request download links simultaneously. Default 7; range 1–30. This is in-flight task count, not requests per second. Try increasing it when downloads wait for links; try reducing it if link timeouts, connection failures or HTTP 429 responses increase. More requests do not guarantee more successful downloads.
Downloads: how many files can wait for a server response or receive data using ready links simultaneously. Default 20; range 1–30. Other files can download while some wait. A timed-out attempt frees its slot. If few links are ready, some slots remain idle.
7 + 20 is the best balance in our tests and our starting recommendation, not a Yandex limit or a universal optimum. You may choose 30 + 30. The app caps each field at 30 to bound connection and memory use. Queues remain bounded instead of spawning a task for every file.
On a slow connection, try fewer downloads, for example 5–10. Files share your total bandwidth; more transfers do not increase it. If the link is already saturated, increasing concurrency usually adds no overall speed. If server waits leave bandwidth idle, increasing it may help. A few large-file transfers may fill the link, while small files depend more on link acquisition.
Change one setting at a time and compare several passes with equal timeouts: successful files, completed data and elapsed time. A fast pass with more failures is not necessarily better. All five transfer settings persist and are locked throughout passes and automatic-retry pauses. Concurrency and link timeout are API-only; Windows and the official desktop client control transfers in client mode.

REQUEST LAUNCH INTERVAL
API launch interval, ms: time between API request launches. Range 25–1000 ms; default 50 ms. Approximate maximum launch rates: 25 ms → 40/s; 50 ms → 20/s; 100 ms → 10/s; 200 ms → 5/s; 1000 ms → 1/s. Requests can wait for responses concurrently without waiting for the preceding response before launching.
Reduce the interval to try a higher launch rate and compare results. Increase it if rate restrictions, HTTP 429 or API instability occur. A lower interval does not guarantee better speed or success. Queueing and the chosen interval do not consume link response time. This setting persists and is locked while running.
Section 2.2 of the official Disk API terms specifies 40 requests per second: https://yandex.ru/legal/disk_api/ru/ . This limits frequency, not the number of tasks waiting concurrently. The default 50 ms leaves headroom below that rate.

</details>
