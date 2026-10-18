# Обновлятор: отмена и таймаут чтения

Изменения на ветке `work/windows-stabilization`, поверх baseline `234fea2f415007a698d0faa834e1136f3d3891fd`. Этапы 0 и 1 плана стабилизации.

## Поведение

- Числовой GitHub prerelease исключается из обычного канала обновлений.
- Сохраняется 90 секунд ожидания HTTP-заголовков; тело имеет отдельный idle-таймаут 30 секунд. Общий предел получения и проверки пакета — 15 минут.
- Пользователь может отменить загрузку кнопкой в настройках. При закрытии окна загрузка сначала отменяется; UI завершает операцию и освобождает ресурсы.
- Отмена или таймаут возвращают доступность запуска прохода и повторной попытки обновления. Ошибки отмены не показываются как авария интерфейса.
- При неудаче удаляется только созданный запросом временный staging с проверкой имени и отсутствия reparse points. Отказ очистки не скрывает исходную ошибку.
- ZIP проверяется по SHA-256 до распаковки. Сохранены allowlist, лимит 100 MiB, обязательные EXE/config, защита data, формат helper и откат установки.

## Тесты

Новые проверки написаны до исправлений и запускались на старом коде:

| Проверка | RED | GREEN |
|---|---|---|
| UpdaterTests.ReleaseChannel | A numeric prerelease was offered to a stable user | PASS |
| UpdaterTests.DownloadReliability | No cancellable, idle-limited download boundary | PASS |
| UpdaterTests.UpdateControls | No cancellable download operation in UI | PASS |

DownloadReliability использует synthetic HTTP response с заголовками и потоком, который игнорирует токен чтения. Проверены ограниченное завершение по idle-таймауту, закрытие потока, явная отмена, очистка staging, корректное обновление после отмены и отказ по неправильной SHA-256.

UpdateControls вызывает реальную операцию UI с подменённым сетевым получением, нажимает обработчик кнопки отмены и проверяет возвращение управления и доступности запуска. Приложение и обновлятор не устанавливаются в пользовательскую папку.

Команды (корень проекта):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Platform x64 -OutputDirectory dist/verify-x64
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-focused.ps1 -TestClass UpdaterTests -Method ReleaseChannel
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-focused.ps1 -TestClass UpdaterTests -Method DownloadReliability
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-focused.ps1 -TestClass UpdaterTests -Method UpdateControls
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-tests.ps1 -OutputDirectory dist/verify-x64
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -Platform x86 -OutputDirectory dist/verify-x86
powershell -NoProfile -ExecutionPolicy Bypass -File tests/run-tests.ps1 -OutputDirectory dist/verify-x86
```

Обе сборки и полные наборы самопроверок завершились с кодом 0, PASS. Полные наборы выполнялись вне песочницы с работающей DPAPI. Отчёты сохранены в `docs/testing/updater-2026-10-18/`. Проверка diff выполнялась с `git -c core.whitespace=cr-at-eol diff --check` в соответствии с CRLF исходников.

Не проверено: реальное обновление пользовательской установки через GitHub, все поддерживаемые Windows/DPI, физическое выключение питания и авария посреди замены EXE. Они не подменяются успешными офлайн-тестами. Независимый обзор всей серии ещё впереди.

## Продолжение

Следующий этап: Task 2 в `docs/superpowers/plans/2026-10-18-windows-stabilization.md`: атомарные настройки, backup/recovery и сохранение зашифрованных байтов DPAPI. Пользователь выбрал ручное переключение моделей. Рекомендована GPT-6 Astra / High / Standard для этого этапа; Fast/Ultrafast и помощники не запускались.
