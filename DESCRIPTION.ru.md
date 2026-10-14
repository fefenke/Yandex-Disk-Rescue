# Yandex Disk Rescue v0.2.7

**Файлы безлимитной автозагрузки фото и видео НЕ учитываются и НЕ скачиваются. Поддерживается только обычный раздел «Файлы».**

## Что изменилось

- API теперь докачивает `.ydr-part` с сохранённого места через HTTP Range, если сервер поддерживает продолжение. При полном ответе сервера загрузка начинается с нуля. Размер и MD5 (если предоставлен API) проверяются перед завершением; смена облачной версии запрещает использование старого фрагмента.
- Новый режим «Докачать начатые файлы · API»: только непустые временные файлы. С автоповтором повторяется именно эта очередь, без него — один проход. Серверные ошибки и таймауты не блокируют остальные файлы. Докачка работает также в других API-проходах.
- Журнал больше не заменяет весь текст при обновлении статистики: сохраняются выделение и положение прокрутки. При чтении старых строк автоматическая прокрутка отключается.
- Сбои загрузки показывают тип исключения и код ошибки. HTTP 5xx относятся к ошибкам сервера, остальные сбои — к таймаутам и сбоям.
- Обновлены встроенный гайд и инструкции. Через официальный клиент загрузкой по-прежнему управляют Windows и клиент Диска.

## Скачать и запустить

[Windows 64 бит](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v0.2.7/YandexDiskRescue-win-x64.zip)

[Windows 32 бит](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v0.2.7/YandexDiskRescue-win-x86.zip)

Распакуйте ZIP целиком и запустите YandexDiskRescue.exe. Требуются Windows 10 1709+ / Windows 11 и .NET Framework 4.7.1+. При обновлении сохраните свою локальную папку `data`: в ней история, список и настройки.

API работает без клиента Диска и его перезапусков, поэтому может быть быстрее; скорость зависит от сети и ответов сервера. Нужен OAuth-токен: [официальный полигон API Яндекс Диска](https://yandex.ru/dev/disk/poligon/). Токен хранится локально через Windows DPAPI; когда доступ больше не нужен, отзовите его в Яндекс ID. Второй способ — через официальный клиент, без ввода токена в программу.

Независимое неофициальное приложение. Ошибки серверов Яндекса не исправляет и скачивание всех файлов не гарантирует.

## English

API downloads now resume partial files using HTTP Range when supported. Full responses restart the file. Cloud version, final size and available MD5 are checked. A dedicated partial-download mode keeps its own queue during automatic retries. The activity log preserves selection and scrolling. Updated guide and download error codes. Windows x64/x86 portable builds; extract the whole ZIP and preserve your local `data` when updating. Unlimited photo/video auto-upload is not supported.
