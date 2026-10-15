# Yandex Disk Rescue 1.0.4

## Русский

Релиз улучшает повторные проходы, обработку ограничений API и сохранность начатых загрузок.

- Убрана обязательная пауза 30 секунд между нормальными проходами. Для временных ошибок всего прохода остаётся отменяемое замедление 1–30 секунд.
- После новых байтов и сетевого обрыва допускаются до двух дополнительных попыток по той же ссылке с продолжением данных и повторным входом в общую очередь. Таймаут повторов: выбранный +2 секунды, например 3 → 5 → 5.
- Общий ограничитель API учитывает Retry-After при 429, 408 и 5xx; без заголовка использует ожидание до 60 секунд, временно снижает частоту и постепенно восстанавливает её. Готовые загрузки продолжаются.
- Подтверждённые данными ссылки хранятся в памяти между автоматическими проходами. Отказ или отсутствие новых байтов приводят к свежей ссылке на следующем проходе.
- Перед докачкой запрашиваются свежие сведения конкретного файла. Недоступность метаданных сохраняет части; смена версии не позволяет смешать данные. Изменённый список сохраняется и при ручной остановке.
- Контрольные точки 32 МиБ / 15 секунд на каждый поток: данные принудительно записываются до журнала SHA-256. Восстановление отбрасывает повреждённый или неподтверждённый хвост. Части проверяются вне сетевого таймаута, сборка повторно использует готовые части.
- Переработаны README RU/EN, полный гайд и встроенная справка. Кнопки x64/x86 расположены отдельно. Гайд встроен в программу и доступен в репозитории; ZIP совместим с обновлятором 1.0.3.

Проверки: полные самопроверки x64/x86, повреждённые части и журналы, остановка, диапазоны, MD5, смена версии. На SATA HDD/NTFS проверены принудительное завершение процесса при загрузке и сборке, восстановление и финальная MD5. Синтетическая запись 2 ГиБ: 77,9 с без контрольных точек, 84,9 с с ними (+7,0 с, около 9% в этом замере). Физическое отключение питания не проводилось; сохранность зависит от накопителя и выполнения flush.

Сохраните data и .ydr-part / .ydr-segments / .ydr-state при обновлении. Части пока хранятся отдельно; сборке нужно дополнительное место. Без облачного MD5 проверяется размер, что слабее проверки содержимого. Неподходящий существующий файл может заменяться после успешного скачивания. При ошибках сканирования полнота списка не гарантируется. Новые строгие статусы, запрет замены и расширенный учёт пропущенных папок в этот релиз не входят.

Поддерживается обычный раздел «Файлы». Безлимитная автозагрузка фото/видео не поддерживается. Недоступные облачные оригиналы могут оставаться недоступными.

## English

This release improves repeated passes, API throttling and recovery of started downloads.

- Removes the mandatory 30-second pause after normal passes. Temporary whole-pass failures retain cancellable 1–30-second backoff.
- Allows up to two additional attempts after new bytes and a network interruption, reusing the signed link and offset while rejoining the shared connection queue. Both retry timeouts equal the selected value +2 seconds, for example 3 → 5 → 5.
- Shares API cooldown and pacing across automatic passes, honoring Retry-After for 429, 408 and 5xx. Without a header, cooldown grows up to 60 seconds. Request frequency recovers gradually; ready-link downloads continue.
- Keeps links proven by data only in session memory. Rejection or zero new bytes causes a fresh link request on the next pass.
- Queries fresh metadata for the individual file before resuming. Failed checks preserve parts, changed versions invalidate old data, and refreshed inventory is also saved on manual Stop.
- Adds per-stream 32 MiB / 15-second checkpoints: file data is flushed before the SHA-256 journal. Recovery discards corrupt or unconfirmed tails. Checks run outside network idle timeout; interrupted merging reuses completed parts.
- Rewrites both readmes, full guides and embedded help. Separate x64/x86 download buttons. The full guide is embedded and available in the repository; ZIPs remain compatible with the 1.0.3 updater.

Validation covers full x64/x86 self-tests, corrupted/torn journals, Stop, byte ranges, MD5 and changed versions. SATA HDD/NTFS tests killed a process during downloading and merging, then verified final MD5. A synthetic 2 GiB write took 77.9 seconds without checkpoints and 84.9 seconds with them (+7.0 seconds, about 9% in that test). Physical power removal was not tested; durability depends on the drive honoring flush commands.

Retain data and .ydr-part / .ydr-segments / .ydr-state when updating. Separate parts still need extra space for merging. Missing cloud MD5 leaves weaker size-only checks. An unsuitable existing file can be replaced after a successful new download. Listing failures prevent completeness guarantees. Strict no-hash statuses, never-overwrite mode and detailed skipped-folder tracking are not included.

Only the regular Files section is supported. Unlimited photo/video auto-upload is unsupported. Unavailable cloud originals may remain unavailable.
