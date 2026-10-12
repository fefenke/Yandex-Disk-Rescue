# Yandex Disk Rescue v0.2.0

**Важно: файлы, загруженные через безлимитную автозагрузку фото и видео, не учитываются и не скачиваются — ни через клиент Диска, ни через API. Поддерживаются только файлы из обычного раздела «Файлы».**


Два способа скачивания: через клиент Яндекс Диска на компьютере или напрямую через API.

**API может работать быстрее:** ему не нужны очередь и перезапуски клиента Диска. Фактическая скорость зависит от сервиса и соединения.

Для API нужен **OAuth-токен Яндекса**: https://yandex.ru/dev/disk/poligon/
Укажите токен, облачную папку (disk:/ — весь Диск) и локальную папку для сохранения.

- До пяти загрузок одновременно; зависшие запросы отменяются.
- Сохранённый список позволяет продолжить без нового обхода; незавершённое сканирование возобновляется.
- «Продолжить» пропускает прошлые завершённые попытки. Повтор ошибок доступен отдельными режимами.
- Общий счётчик «Обработано» включает прошлые попытки.
- В API проверяются размер и доступный MD5, история отделена от клиентского режима.
- Токен зашифрован Windows DPAPI и не включается в настройки JSON, журналы, отчёты или архив выпуска.

Windows 10 1709+ / Windows 11 x64, .NET Framework 4.7.1+. Распакуйте ZIP целиком и запустите Start Yandex Disk Rescue.cmd. Клиентский режим требует установленного и запущенного Яндекс Диска; при ошибках синхронизации клиент перезапускается.

Безлимитная автозагрузка не поддерживается. Программа не гарантирует скачивание файлов, которые сервер не отдаёт. Предварительная версия без цифровой подписи. API проверен на имитации сервера; совместимость с реальным сервисом требует пользовательского тестирования.

---

Two download methods: desktop Yandex Disk client or direct OAuth API.
API mode can be faster because it does not need the desktop client's queue or restarts; speed depends on the service and network.
Obtain a Yandex OAuth token with Disk read access: https://yandex.ru/dev/disk/poligon/
Five concurrent downloads, idle cancellation, resumable inventory scans, saved-list mode, cumulative processing progress, dedicated retries, size/MD5 verification and Windows-encrypted token storage.
Extract the complete ZIP and run Start Yandex Disk Rescue.cmd. Windows x64 only. Unlimited auto-upload is unsupported. Unsigned preview release.