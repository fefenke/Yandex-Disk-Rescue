# Yandex Disk Rescue v0.2.6

**Безлимитная автозагрузка НЕ учитывается и НЕ скачивается. / Unlimited auto-upload files are NOT counted or downloaded.**

Подключение и скан API повторяют временные ошибки в том же запросе с сохранением папки и страницы. Первая повторная попытка сразу, затем паузы 1, 2 и до 5 секунд; Retry-After сервера учитывается. Стоп отменяет ожидание.

Отдельная галка увеличения ожидания скачивания работает только с автоповтором: первые два завершённых непустых прохода с начальным значением, следующие два +2 секунды, максимум 5. С 1 сек: 1,1,3,3,5; с 3 сек: 3,3,5. Значения 5 и больше неизменны. Ошибки подключения и скана не считаются проходами. Новый запуск сбрасывает последовательность, фактическое ожидание записывается в статистику. Гайд и README обновлены.

API connection/scan requests retry temporary failures at the same position, independently of file passes. Immediate first retry, then 1/2/5-second delays and server Retry-After. Optional adaptive download waiting increases every two completed nonempty passes up to 5 seconds. Connection failures and scans do not advance the schedule. Each session restarts at its initial setting; actual timeout is recorded in pass history.

Portable Windows x64/x86; extract ZIP completely. Preserve your local data when updating. No personal tokens, logs or history included. OAuth: https://yandex.ru/dev/disk/poligon/
