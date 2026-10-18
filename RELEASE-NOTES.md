# YDR 1.2.0 — стабилизация загрузки / download reliability

> **Исправленные пакеты 1.2.0 от 19 октября:** обе галочки обновления облачного списка выключены по умолчанию и однократно выключаются в старых настройках. Последующий ручной выбор сохраняется; другие настройки не сбрасываются. Если списка ещё нет, он получается автоматически. Если уже установлена первоначальная 1.2.0, скачайте исправленный ZIP вручную и замените файлы приложения, сохранив data: номер версии не изменён.
>
> **Corrected 1.2.0 packages, October 19:** both cloud-list refresh options default to off and are turned off once in older settings. Later manual choices are retained; other settings are not reset. A missing inventory is fetched automatically. Existing users of the original 1.2.0 should download the corrected ZIP manually and replace app files while keeping data; the version number is unchanged.

## Русский

Программа сохраняет доступные файлы и прогресс, чтобы повторять проблемные загрузки. HTTP 500, недоступность и другие ошибки серверов Яндекса могут оставаться; успешный конец прохода не означает, что скачан каждый файл.

- Только API: установленный клиент Яндекс Диска больше не требуется. Старые настройки и история Desktop остаются совместимыми; клиентская история не импортируется в API.
- Пять режимов: проверка всех файлов, всё недокачанное, ошибки загрузки, неполные файлы и несовпадения хешей. Ошибки выбираются только для той же облачной версии. Проверка всех при автоповторе переходит к недокачанному; несовпадения хешей получают один проход на ручной запуск.
- Назначения локальных путей сохраняются между сканированием, кешем и перезапусками; неоднозначные конфликты блокируются до записи. Существующие файлы и частичные данные защищены от случайного переназначения.
- Настройки и DPAPI-токен сохраняются атомарно. Есть восстановление настроек из резервной копии и видимое сообщение при отказе записи. Токен не записывается в JSON настроек; не включайте свою папку data в публичные архивы.
- Скачивание обновлений можно отменить. Предварительные выпуски не предлагаются; ожидания ограничены, неиспользованные временные пакеты очищаются.
- Новая установка: «Скачать всё недокачанное», обе галочки обновления облачного списка выключены; автоповторы и проверка обновлений включены. Таймауты 3 + 3 секунды, параллелизм 7 ссылок / 20 загрузок, 4 потока для файлов от 256 МБ. Ранее сохранённые настройки не заменяются тестовым профилем.

Windows 10 1709+ / Windows 11, .NET Framework 4.7.1+, отдельные ZIP x64 и x86. При обновлении сохраняйте data и папку скачиваний. Переход на .NET 10 в этот выпуск не входит.

Проверено: полные офлайн-наборы x64/x86 и реальный аккаунт — загрузка, перезапуск, отсутствие повторного скачивания готового, MD5, остановка и продолжение. Во втором реальном тесте подтверждены 272 готовых файла с совпадающим MD5; оставшиеся серверные/сетевые ошибки сохраняются для повторов. Все версии Windows и конфигурации DPI отдельно не проверялись.

## English

YDR keeps available files and progress so you can retry problem downloads. Yandex HTTP 500 responses and other server failures may persist; a completed pass does not guarantee that every file was downloaded.

- API-only downloads: the installed Yandex Disk client is no longer required. Legacy settings and Desktop pass history remain readable; client history is not imported into API history.
- Five modes with independent cloud-list refresh. Download-error selection is version-specific; automatic repeats after Check all files process remaining files. Checksum-failure mode runs once per manual Start.
- Persistent local destination ownership across scans, cached passes and restarts. Ambiguous conflicts are rejected before writes, protecting existing files and partial data.
- Atomic settings and DPAPI token persistence, settings backup recovery and visible save failures. Tokens are excluded from settings JSON; keep your personal data folder out of public archives.
- Cancellable update downloads, stable-release selection, bounded waits and unused-package cleanup.
- Fresh-install defaults: Download all remaining files; both cloud-list refresh options disabled; automatic retries and update checks enabled. Timeouts 3 + 3 seconds, link/download concurrency 7/20, four streams from 256 MB. The temporary live-test profile is not shipped.

Windows 10 1709+ / Windows 11, .NET Framework 4.7.1+, separate x64/x86 ZIPs. Keep data and downloaded files when updating. No .NET 10 migration in this release.

Validated with full x64/x86 offline suites and a live account: download, restart, completed-file reuse, MD5, Stop and resume. The larger live test verified 272 completed files against MD5; remaining server/network failures remain retryable. Not every Windows version or DPI configuration was exercised.

---

<details>
<summary>1.1.2 — historical notes / прежний выпуск</summary>

<details>
<summary>Изменения 1.1.2 · RU / EN</summary>

- Исправлено сохранение неполного облачного списка и повторное чтение непрочитанных папок.
- Старые списки с ошибками восстанавливаются автоматически при первом запуске прохода после обновления. Готовые файлы и история сохраняются; полная проверка локальных хешей для этого не нужна.

---

- Fixed incomplete cloud inventories and retries of unreadable folders.
- Old inventories with scan errors rebuild automatically on the first Start after updating. Completed local files and history are retained; full local hash verification is not required for this repair.

</details>

Способ через официальный клиент временно скрыт после частичного восстановления работы Яндекс Диска; при возвращении зависаний его можно будет вернуть.

The official-client method is temporarily hidden after a partial recovery of Yandex Disk functionality; it may return if stalls recur.

</details>
