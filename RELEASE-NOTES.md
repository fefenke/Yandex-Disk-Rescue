# Yandex Disk Rescue v0.2.3

**Файлы безлимитной автозагрузки НЕ учитываются и НЕ скачиваются. Только обычные файлы из раздела «Файлы».**

- «Продолжить · только непроверенные» пропускает завершённые попытки и обрабатывает оставшиеся файлы.
- «Перепроверить все файлы» повторно проверяет весь список, включая скачанные.
- Верхний выбор API отвечает только за список: сохранённый скан или обновление из облака. Нижний выбор задаёт режим обработки.
- Сохранена ускоренная сверка ранее проверенных неизменившихся файлов.
- ZIP для Windows x64 и x86. Полностью распакуйте архив и запускайте приложение из постоянной папки.

**Unlimited photo/video auto-upload files are NOT counted or downloaded. Only regular Disk files are supported.**

Continue processes unchecked files; Recheck all files verifies the full inventory. The upper API selector chooses cached inventory or cloud refresh. The lower selector chooses which files to process. Extract the complete portable ZIP before launching.

OAuth API requires a token: https://yandex.ru/dev/disk/poligon/
API avoids desktop-client restarts and can be faster; actual speed depends on server availability.
Новые отдельные режимы: только ошибки; только нулевая загрузка; ошибки и нулевая загрузка вместе; только новые или изменённые файлы; продолжить незавершённый проход; перепроверить все файлы. Для новых облачных файлов сначала обновите список API.

Separate modes: errors only, zero downloads only, errors plus zero downloads, new/changed files only, resume unfinished pass, recheck all files. Refresh API inventory to discover new cloud files.

## Автоповтор и статистика проходов
Галка «Повторять проблемные файлы» автоматически повторяет ошибки и таймауты с паузой 30 секунд. Скачанные файлы не включаются. Стоп отменяет загрузку и ожидание. Автоповтор прекращается, когда проблемные файлы закончились, при критической ошибке или неподтверждённой отмене. В API повторные проходы используют сохранённый список.
Вкладка «Проходы» сохраняет локальную статистику: время, длительность, режим, результат, обработано, скачано за проход и всего, осталось, нулевая загрузка и ошибки. Личная статистика и токены не входят в ZIP.

## Automatic retries and pass statistics
Repeat problem files retries errors and timeouts with a 30-second pause. Stop cancels both downloads and the wait. Downloaded files are excluded; the loop stops when there are no retry candidates, on fatal errors, or uncertain cancellation. API retries reuse cached inventory. Pass history stores local per-pass download counts, duration, outcome and remaining files. No private statistics or tokens are packaged.
