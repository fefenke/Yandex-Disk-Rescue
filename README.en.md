<div align="center">
<p><strong>If the app helped save your files, you can support the author 💜</strong><br>Optional, always. The app remains free.</p>
<a href="https://yoomoney.ru/fundraise/1KS67440FP0.261012"><img alt="Support_the_author" src="https://img.shields.io/badge/Support_the_author-ЮMoney-7556D9?style=for-the-badge"></a>
</div>

![Yandex Disk Rescue](https://raw.githubusercontent.com/fefenke/Yandex-Disk-Rescue/main/assets/readme-cover.svg)

<div align="center">

[![Build](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml/badge.svg)](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml)
[![1.0.0 release](https://img.shields.io/badge/1.0.0-release-5965E9)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/tag/v1.0.0)
[![MIT](https://img.shields.io/badge/license-MIT-208B74)](LICENSE)

[Русский](README.md) · **English** · [Full guide](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/GUIDE.en.md)

</div>

Free Windows app for downloading available Yandex Disk files, keeping history and retrying failures. Use the API with a token or the official desktop client without entering a token into this app.

> [!IMPORTANT]
> **Unlimited photo/video auto-upload files are NOT counted or downloaded.** Only the regular **Files** section is supported. The app does not repair server failures or guarantee every download.

## Download

[![Windows x64](https://img.shields.io/badge/Windows_64_bit-x64_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.0.0/YandexDiskRescue-win-x64.zip)
[![Windows x86](https://img.shields.io/badge/Windows_32_bit-x86_ZIP-5965E9?style=for-the-badge)](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.0.0/YandexDiskRescue-win-x86.zip)

Windows **10 1709+ / 11** · .NET Framework **4.7.1+** · RU / EN · portable.

**Version 1.0.0.** Extract the complete ZIP to a permanent folder and run **Start Yandex Disk Rescue.cmd**.

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

Automatic retries run problem passes after 30-second pauses without rescanning. Partial mode keeps its partial-only queue. Idle timeout limits waiting without new data, including the initial API link/response, not total download duration. Try **1 second** for a quick pass and **3–5** for repeated timeouts. Optional gradual waits follow **1,1,3,3,5**; starting at 3 gives **3,3,5**. A new session resets the sequence.

API resume uses HTTP Range if supported and the cloud version matches. Size and available MD5 are checked before completion. HTTP 5xx, timeouts and IOException are different causes; IOException alone does not prove a server error.

</details>

<details>
<summary><strong>Interface and updates</strong></summary>

Double-click a completed file to open it or a folder to browse. Context menus offer Open and Show in Explorer. Tooltips show full names; columns and panels resize. The log keeps selection/scroll position and offers All events / Successful / Errors filters. Overview hides absent categories.

Disable automatic update checks in Settings if preferred; manual Check for updates and Update remain available. Automatic checks run on startup and every 6 hours. Installation requires approval while idle. The architecture-specific ZIP is verified against GitHub's SHA-256 digest. Only program files are replaced; `data` retains history, inventory, settings and the encrypted token. Close the app and preserve `data` for manual updates too.

</details>

## Security and help

The token is stored locally using **Windows DPAPI**, bound to your Windows account, and decrypted in memory when needed. Storage encryption does not replace trust in the code. Never publish your `data` folder or share tokens; revoke access when no longer needed.

[Full guide](https://github.com/fefenke/Yandex-Disk-Rescue/blob/main/GUIDE.en.md) · [Report a bug](https://github.com/fefenke/Yandex-Disk-Rescue/issues) · [Release notes](https://github.com/fefenke/Yandex-Disk-Rescue/releases/tag/v1.0.0)

Include the app version, download method and error text. Exclude tokens, personal paths and your full history.

<details>
<summary>Build from source</summary>

```powershell
.uild.ps1 -Package -Platform x64
.uild.ps1 -Package -Platform x86
```

Windows and .NET Framework 4.7.1+ required. Run `YandexDiskRescue.exe --self-test` for checks. [CI builds](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml).

</details>

Independent unofficial project, unaffiliated with and not endorsed by Yandex. Service names belong to their respective owners. [MIT license](LICENSE).
