[Windows x64](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.2.4/YandexDiskRescue-win-x64.zip) | [Windows x86](https://github.com/fefenke/Yandex-Disk-Rescue/releases/download/v1.2.4/YandexDiskRescue-win-x86.zip)

# YDR 1.2.4 — публичные ссылки / public sharing links

## Русский

- Два независимых источника: «Мой Яндекс Диск» через OAuth и «Публичная ссылка» без токена. Каждый режим запоминает собственную папку сохранения; одинаковую папку можно выбрать вручную.
- Публичные одиночные файлы и вложенные папки скачиваются с сохранением структуры. Исправлены корневой path="/", обновление истёкших ссылок и проверка идентичности при докачке.
- Публичные API-запросы и подписанные ссылки не получают OAuth. Существующие проверки MD5/SHA-256, защита путей и сохранение конфликтующих копий остаются включены.
- Неполный список обозначается явно. Отсутствующий хеш отмечается size_only. Ограничения доступа и серверные ошибки Яндекса не обходятся.
- Обновлены вкладки источников, скруглённые поля, компактная справка и предупреждение о публичном доступе. Фиксированная сетка исключает сдвиг полей; сохранение выбора источника выполняется в фоне.
- Актуализированы встроенный гайд и документация RU/EN. Основано на PR #2.

## English

- Two independent sources: My Yandex Disk through OAuth and Public link without a token. Each remembers its own destination; users may manually choose the same folder.
- Download public files and nested folders while retaining their structure. Fixed root path="/", expired URL refresh and resource identity checks before resume.
- Public API requests and signed download links receive no OAuth header. MD5/SHA-256 checks, destination protections and preservation of conflicting copies remain enabled.
- Incomplete inventories are explicitly identified; missing hashes are labelled size_only. Yandex access restrictions and server errors are not bypassed.
- Updated source tabs, rounded inputs, compact help and a public-access warning. A fixed layout prevents field movement; source selection saves in the background.
- Updated embedded help and RU/EN documentation. Based on PR #2.
