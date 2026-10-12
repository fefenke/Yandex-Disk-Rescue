# Yandex Disk Rescue v0.2.1

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