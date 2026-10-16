<details>
<summary>Изменения 1.0.5 · RU / EN</summary>

## Русский

- Диапазоны записываются в один временный файл с картой SHA-256. На NTFS не нужна финальная сборка отдельной полной копии.
- Перед заменой сохраняются прежние файлы; новые копии с плохим хешем тоже остаются. Ошибочная облачная версия не скачивается бесконечно после пересканирования или удаления её сохранённой копии.
- Сначала размер, затем MD5; SHA-256 используется только без MD5. Отсутствие обоих хешей обозначено явно.
- При возобновлении сканирования незавершённая папка читается с первой страницы. Завершённые папки сохраняются в быстром режиме; новые изменения в них ищутся обновлением списка.
- Обновлены гайды, описание решений и публичная матрица тестов. Основной запуск — YandexDiskRescue.exe; CMD дополнительно проверяет окружение.

Сохраните data и служебные файлы загрузок при обновлении. Старые части переносятся после проверки карты. Полные проверки x64/x86 выполняются до публикации, результаты доступны в Actions. На HDD проверялось завершение отдельного процесса; физическое отключение питания не проводилось. При изменениях облака во время сканирования полнота списка не гарантируется.

## English

- Ranges write into one payload with a SHA-256 extent map; NTFS needs no final full-file merge.
- Existing copies are preserved before replacement, failed new copies are retained, and bad cloud versions are blocked from endless automatic downloads.
- Size is checked first, then MD5, or SHA-256 only when MD5 is absent. Missing both hashes is explicitly marked.
- Interrupted folders restart at their first page; saved-list mode retains completed folders. Refresh the inventory to discover later changes there.
- Updated guides, design decisions and public test matrix. Launch YandexDiskRescue.exe directly; the optional CMD checks prerequisites.

Retain data and download sidecars when updating. Legacy parts migrate after map validation. Full x64/x86 checks run before publication; reports are available in Actions. HDD testing used dedicated process termination, not physical power removal. Concurrent cloud edits prevent an atomic completeness guarantee.

</details>
