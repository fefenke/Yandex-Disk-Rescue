# Проверки по аспектам

[Гайд](GUIDE.md) · [Решения](DESIGN-DECISIONS.md) · [GitHub Actions](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml)

Локальные полные самопроверки x64/x86 для безопасных копий и карты прошли 16 октября 2026 года. Сценарий перечитывания незавершённой папки добавлен отдельно после этого: сначала воспроизведён пропуск нового файла, затем проверено исправление. Актуальный статус CI привязан к конкретному коммиту в Actions; прошлый успешный прогон не является результатом будущего коммита.

## Матрица

| Направление / Aspect | Где проверяется | Что проверяется |
|---|---|---|
| Целостность / Integrity | [SafeDownloadTests](src/SafeDownloadTests.cs) | Размер, приоритет MD5, SHA-256 без MD5, отсутствие хешей |
| Сохранение копий / Preservation | [SafeDownloadTests](src/SafeDownloadTests.cs) | Прежний файл остаётся до окончания загрузки; архивы; recovery между перемещениями |
| Ошибочные версии / Failure ledger | [SafeDownloadTests](src/SafeDownloadTests.cs) | Пересканирование, удалённая копия, ручной повтор, дедупликация, новая облачная версия |
| Карта / Range map | [RangeMapTests](src/RangeMapTests.cs) | Параллельные позиции, неподтверждённые данные, повреждённые участки и JSON, границы старого плана |
| Очистка последствий 1.0.5 / Cleanup | [PreservedCleanupTests](src/PreservedCleanupTests.cs) | Только пустые принадлежащие программе sparse-заготовки; защита непустых, неподтверждённых, чужих и занятых файлов; одноразовый запуск, отмена и продолжение; пустые вложенные папки и переход с уже завершённой очистки 1.0.6 |
| Остановка подготовки / Preparation Stop | [PreparationStopTests](src/PreparationStopTests.cs) | Отмена между записями истории, сохранность журнала и отсутствие лишних полных отчётов при остановке до очереди загрузок |
| Аварийное завершение / Process kill | [RangeMapTests](src/RangeMapTests.cs), [DurablePartialTests](src/DurablePartialTests.cs) | Убитый дочерний процесс; доверие подтверждённым байтам |
| Старые части / Legacy migration | [SegmentTests](src/SegmentTests.cs), [ApiTests](src/ApiTests.cs) | Докачка, смена версии, сохранение прогресса, неподдерживаемый Range |
| Ссылки / Signed links | [SignedLinkTests](src/SignedLinkTests.cs) | Свежие сведения начатого файла, подтверждение ссылки данными, отказ ссылки и изоляция контекстов |
| Повторы / Retries | [TransferRetryTests](src/TransferRetryTests.cs) | Повтор только после новых байтов, лимиты, добавочные 2 секунды, очередь и Stop |
| API и 429 / Pacing | [ApiPacerTests](src/ApiPacerTests.cs) | Retry-After, конкурентные ошибки, сохранение cooldown между проходами, отмена |
| Очереди / Queues | [ApiTests](src/ApiTests.cs), [SegmentTests](src/SegmentTests.cs) | Независимые лимиты ссылок/скачиваний, общий бюджет частей, отсутствие таймаута в очереди |
| Сканирование / Enumeration | [ApiTests.ScanResumeTests](src/ApiTests.cs) | Сначала сохранены 1000 записей, затем файл добавлен в начало; новая первая страница прочитана, файл найден, дублей нет |
| Токен и пути / Security boundaries | [ApiTests](src/ApiTests.cs), [SafeDownloadTests](src/SafeDownloadTests.cs) | OAuth не отправляется на download-хост; DPAPI; отсутствие plaintext в настройках; безопасные пути и конфликты имён |
| Обновление / Updater | [UpdaterTests](src/UpdaterTests.cs) | Архитектура, контрольная сумма, допустимый состав ZIP и безопасная установка |
| UI и история / UI and history | [Tests](src/Tests.cs), [PassTransitionTests](src/PassTransitionTests.cs) | Каталог, прогресс, настройки, статусы, переходы проходов и сохранение результатов |

## Как воспроизвести

Запускайте в отдельной копии исходников под Windows. Тесты используют временные синтетические данные и подменённые HTTP-ответы, а не личный аккаунт или токен. Проверки аварийного завершения запускают и завершают собственный дочерний процесс.

```powershell
.\build.ps1 -Platform x64
.\tests\run-tests.ps1

.\build.ps1 -Platform x86
.\tests\run-tests.ps1
```

Для отдельной выходной папки сначала соберите её через `build.ps1 -OutputDirectory <папка>`, затем передайте ту же папку в `run-tests.ps1`. Итог — `self-test-results.txt` рядом с тестовым EXE. GitHub Actions запускает обе архитектуры и сохраняет этот отчёт отдельно от ZIP приложения. Исходники тестов опубликованы в `src`; они входят в сборку для режима `--self-test`.

## HDD и питание

На HDD E: в отдельной синтетической папке прошли проверки карты и сохранения копий. После Kill процесса карта восстановила 32 МиБ подтверждённых данных, не приняв неподтверждённый хвост. Папка теста удалена после успеха; работающие загрузки и личные бэкапы не затрагивались. Подробности предыдущей проверки — в [локальном отчёте реализации](docs/superpowers/plans/2026-10-16-safe-single-file-downloads-verification.md).

Физическое отключение питания не выполнялось. Эти тесты не доказывают выполнение flush любым накопителем или контроллером. Старый замер 2 ГиБ в гайде относится к прежнему формату и не является замером новой карты. Реальные сетевые скорости, доступность повреждённых облачных оригиналов и полнота постоянно меняющегося облака синтетическими тестами не гарантируются.

## English

The table links each verification aspect to its executable test source. Full local x64/x86 checks passed for the safe-copy/range-map implementation; interrupted-folder enumeration has a separate regression test. The current commit's CI status is visible in Actions. Network responses and credentials are synthetic. HDD testing killed a dedicated child process, not the user's running application. Physical power removal and arbitrary drive/controller durability were not tested. Reports describe their exact scope rather than certifying universal safety or completeness.

## Быстрый повторный запуск и размер из API

[StartupReliabilityTests](src/StartupReliabilityTests.cs) проверяет строгий размер в ответе API (отсутствие, null, строка, дробь, отрицательное значение; настоящий ноль разрешён), исправление старого неизвестного нулевого размера, быструю проверку неизменённого файла и полную проверку изменённого содержимого при сохранённой дате. Проверки индекса включают дописывание журнала, оборванную строку, повреждение индекса, сохранение сведений о проверке и отмену.

`history.latest` содержит последние записи истории и контрольную сумму. Он используется только при совпадении пути, длины и времени изменения исходного `history.jsonl`. При отсутствии или повреждении индекса программа читает исходный журнал. Журнал не сокращается. После первого чтения старой истории индекс сохраняется при закрытии хранилища; следующий запуск может использовать его. Полный режим по-прежнему читает файлы для проверки хешей. Индекс содержит локальные сведения и не входит в публикуемые архивы.

Проверка интерфейса 1.0.8 подтверждает, что старые настройки с выключенным API и смена языка не возвращают скрытый способ в список: доступен только API. Логика официального клиента с перезапуском сохранена в коде.
