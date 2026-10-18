# Проверки по аспектам

[Гайд](GUIDE.md) · [Решения](DESIGN-DECISIONS.md) · [GitHub Actions](https://github.com/fefenke/Yandex-Disk-Rescue/actions/workflows/build.yml)

Результаты стабилизации Windows от 18 октября 2026 года собраны в [итоговом отчёте](docs/testing/2026-10-18-windows-stabilization.md), включая точные границы локальной проверки и результаты x64/x86. Актуальный статус CI привязан к конкретному коммиту в Actions; прошлый успешный прогон не является результатом будущего коммита.

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
| Настройки и токен / Atomic settings | [SettingsStoreTests](src/SettingsStoreTests.cs) | Отказ замены, занятые/недоступные файлы, резервная копия, восстановление, уведомления UI и DPAPI |
| Отмена сохранения / Preservation cancellation | [PreserverCancellationTests](src/PreserverCancellationTests.cs) | Отмена хеширования до intent; исходные байты и повторный запуск |
| Назначения / Destination ownership | [DestinationPathsTests](src/DestinationPathsTests.cs) | Конфликты файла/папки/служебного пути, регистр, обновление и прерванный обход, сохранность частей |
| Пять режимов / Five modes | [ModePolicyTests](src/ModePolicyTests.cs) | Версии ошибок, независимость обновления списка, однопроходный ручной повтор, аварийное сохранение истории |
| Только API / API-only | [ApiOnlyTests](src/ApiOnlyTests.cs) | Старые настройки, отсутствие Cloud Files в prerequisites, отчёты, Stop до/во время Run, сохранность клиентской истории |
| Runner | [runner-tests.ps1](tests/runner-tests.ps1) | Таймаут, завершение только своего процесса, сохранность соседнего процесса, устаревший отчёт и параметры сборки |

## Как воспроизвести

Запускайте в отдельной копии исходников под Windows. Тесты используют временные синтетические данные и подменённые HTTP-ответы, а не личный аккаунт или токен. Проверки аварийного завершения запускают и завершают собственный дочерний процесс.

```powershell
.\build.ps1 -Platform x64 -OutputDirectory dist/verify-x64
.\tests\run-tests.ps1 -OutputDirectory dist/verify-x64 -Platform x64

.\build.ps1 -Platform x86 -OutputDirectory dist/verify-x86
.\tests\run-tests.ps1 -OutputDirectory dist/verify-x86 -Platform x86

.\tests\runner-tests.ps1
```

Runner запускает настоящий EXE выбранной сборки, поэтому x86-проверки выполняются в x86-процессе. При отсутствии EXE runner собирает указанную выходную папку и архитектуру. Лимит выполнения — 15 минут; при превышении выдаётся отдельная ошибка timeout и завершается только запущенный тестовый процесс. Параметр `-TimeoutSeconds` позволяет сократить лимит для синтетической проверки. Старый `self-test-results.txt` удаляется перед запуском, чтобы не принять прошлый результат за новый.

Итог — `self-test-results.txt` рядом с тестовым EXE. GitHub Actions запускает обе архитектуры и сохраняет этот отчёт отдельно от ZIP приложения. Исходники тестов опубликованы в `src`; они входят в сборку для режима `--self-test`. DPAPI-проверки требуют обычной Windows-сессии с доступным пользовательским хранилищем ключей; ограниченная песочница может не предоставлять его.

## HDD и питание

На HDD E: в отдельной синтетической папке прошли проверки карты и сохранения копий. После Kill процесса карта восстановила 32 МиБ подтверждённых данных, не приняв неподтверждённый хвост. Папка теста удалена после успеха; работающие загрузки и личные бэкапы не затрагивались. Подробности предыдущей проверки — в [локальном отчёте реализации](docs/superpowers/plans/2026-10-16-safe-single-file-downloads-verification.md).

Физическое отключение питания не выполнялось. Эти тесты не доказывают выполнение flush любым накопителем или контроллером. Старый замер 2 ГиБ в гайде относится к прежнему формату и не является замером новой карты. Реальные сетевые скорости, доступность повреждённых облачных оригиналов и полнота постоянно меняющегося облака синтетическими тестами не гарантируются.

## English

The table links each verification aspect to its executable test source. The [Windows stabilization report](docs/testing/2026-10-18-windows-stabilization.md) records local x64/x86 results and limits. The runner enforces a 15-minute deadline, terminates only its own test process, rejects a missing/stale report and forwards output directory/architecture when a build is needed. Run `tests/runner-tests.ps1` for short synthetic timeout tests. DPAPI checks require a Windows user session with access to its key store. The current commit's CI status is visible in Actions. Network responses and credentials are synthetic. Physical power removal and every supported Windows version/DPI/real network combination have not been tested.

## Быстрый повторный запуск и размер из API

[StartupReliabilityTests](src/StartupReliabilityTests.cs) проверяет строгий размер в ответе API (отсутствие, null, строка, дробь, отрицательное значение; настоящий ноль разрешён), исправление старого неизвестного нулевого размера, быструю проверку неизменённого файла и полную проверку изменённого содержимого при сохранённой дате. Проверки индекса включают дописывание журнала, оборванную строку, повреждение индекса, сохранение сведений о проверке и отмену.

`history.latest` содержит последние записи истории и контрольную сумму. Он используется только при совпадении пути, длины и времени изменения исходного `history.jsonl`. При отсутствии или повреждении индекса программа читает исходный журнал. Начиная с 1.0.9 большой избыточный журнал безопасно сокращается после успешного прохода. После первого чтения старой истории индекс сохраняется при закрытии хранилища; следующий запуск может использовать его. Полный режим по-прежнему читает файлы для проверки хешей. Индекс содержит локальные сведения и не входит в публикуемые архивы.

Проверки интерфейса и фасада подтверждают, что старые настройки с выключенным API и смена языка сохраняют API-запуск. Выбор способа, клиентский движок, поиск/перезапуск клиента и probe удалены. Старые строки Desktop и счётчики отображаются как история; файлы клиентской истории не мигрируют в API.

## Перенос и ограничение накоплений 1.0.9

[StartupReliabilityTests](src/StartupReliabilityTests.cs) проверяет перенос legacy-карт, устойчивый контекст, SHA-256 участков и владельца архива. [RelocatedArchiveRepairTests](src/RelocatedArchiveRepairTests.cs) проверяет восстановление участков, сохранение текущего прогресса и исходников, запрет записи при конфликте, очистку только пустых sparse-пар, существующие готовые файлы, чужой контекст, повреждение и отмену.

[HistoryCompactionTests](src/HistoryCompactionTests.cs) проверяет сокращение журнала, сохранение всех полей, отмену, отказ атомарной замены и обновление индекса. [DataRetentionTests](src/DataRetentionTests.cs) проверяет последние 1000 проходов и 50 отчётов, повреждённый хвост, перенос пути отчёта, чужие, занятые и неизвестные каталоги. Блокировки запрещают конкурентную запись во время замены журнала. Проверки выполняются на синтетических данных.

## Режимы и интерфейс 1.1.1

[ScanControlsTests](src/ScanControlsTests.cs) проверяет пять режимов, зависимость галочек облачного сканирования, переходы автоматических проходов, защиту ошибочных хешей и выбор новых файлов. Проверки разметки охватывают поля, кнопки, галочки, отступ после выбора папки, колонку значков при масштабировании и начальную высоту окна. Подробнее — [отчёт](docs/testing/2026-10-17-cloud-scan-controls.md). Это не проверка всех сочетаний мониторов, DPI и системных шрифтов.

## Релиз 1.2.0 и реальный аккаунт

По указанию владельца обе галочки обновления списка возвращены в выключенное состояние, включая однократное исправление старых настроек; номер выпуска сохранён: [проверки исправления](docs/testing/2026-10-19-cloud-refresh-defaults-correction.md).

Полные релизные проверки x64/x86, настройки по умолчанию и границы проверки приватности: [отчёт 1.2.0](docs/testing/2026-10-19-release-1.2.0.md). Загрузка, повторное использование готовых файлов, MD5, остановка и продолжение на реальном аккаунте: [обезличенный отчёт](docs/testing/2026-10-19-live-account-verification.md). Наличие недоступных файлов с серверными ошибками не считается провалом сценария восстановления.

## Полнота облачного списка 1.1.2

[InventoryCompletenessTests](src/InventoryCompletenessTests.cs) воспроизводит ошибочную отметку завершённости и проверяет повтор необследованных папок, восстановление старых списков, отмену, ошибку на следующей странице и сохранение неполного состояния при обновлении метаданных файла. Подробнее — [отчёт](docs/testing/2026-10-17-incomplete-cloud-inventory.md). Полные офлайн-проверки исправления x64/x86 прошли 17 октября 2026 года; публикация дополнительно проверяется GitHub Actions.
