[Windows x64](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.2.3/YandexDiskRescue-win-x64.zip) | [Windows x86](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.2.3/YandexDiskRescue-win-x86.zip)

# YDR 1.2.3 — очистка остатков другого профиля / cleanup across profiles

## Русский

- Исправлена очистка пустых заготовок в «Сохранённых прежних файлах», когда архив был создан другим профилем загрузки. В 1.2.2 такие остатки могли сохраняться при переключении между всем Диском и отдельной папкой.
- Очистка находит профиль-владелец и проверяет его журнал. Удаляются только доказанно пустые sparse-заготовки с корректной картой и без записанных блоков. Файлы с данными или неопределённым состоянием сохраняются.
- Добавлен регрессионный тест. Настройки, таймауты и облачные файлы не изменяются.

## English

- Fixed cleanup of empty archived download placeholders created by another download profile. Version 1.2.2 could leave these behind after switching between the whole Disk and a subfolder.
- Cleanup resolves the archive's owning profile and checks its records. Only sparse placeholders with valid empty maps and no allocated data ranges are removed; written data and uncertain states are preserved.
- Added a regression test. Settings, timeouts and cloud files are unchanged.
