# Troubleshooting

← [Назад к README](../README.md) · [← Безопасность](SECURITY.md) · [Далее: Changelog →](CHANGELOG.md)

---

## Диагностика

### Логи

```bat
:: Файловый лог
type "C:\Program Files\PaswordCh\Logs\service.log"

:: В реальном времени
powershell -Command "Get-Content 'C:\Program Files\PaswordCh\Logs\service.log' -Wait -Tail 30"

:: EventLog
powershell -Command "Get-EventLog -LogName Application -Source PaswordChService -Newest 30 | Format-Table -AutoSize"
```

### Служба

```bat
sc query PaswordChService
sc qc    PaswordChService
```

### Проверки

```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" ping
"C:\Program Files\PaswordCh\PaswordChService.exe" whoami
"C:\Program Files\PaswordCh\PaswordChService.exe" nextrun
"C:\Program Files\PaswordCh\PaswordChService.exe" testmail
```

---

## Частые проблемы

### Служба не стартует (сразу останавливается)

**Причина:** ошибка в `my.ini` или в шаблонах.

**Решение:** запустите `console` и посмотрите, на какой строке падает:

```bat
sc stop PaswordChService
"C:\Program Files\PaswordCh\PaswordChService.exe" console
```

Обычно это:
- невалидный `Mode` (`Local` / `AD`);
- отсутствует `my.ini`;
- ошибка расшифровки `ENC:` (файл скопирован с другого ПК);
- нет прав на запись в `Logs\`.

---

### `Не удалось расшифровать секрет из my.ini`

**Причина:** `ENC:`-значения привязаны к машине. Файл скопирован на другой сервер.

**Решение:**

1. На исходной машине: `PaswordChService.exe unseal` — превратить секреты обратно в открытые.
2. Перенести `my.ini`.
3. На новой машине: служба при старте сама зашифрует их заново. Или вручную: `seal`.

Если исходная машина недоступна — перешифровать заново на текущей:

```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" encrypt <пароль>
:: Получите ENC:...
:: Вставьте в my.ini
```

---

### `GetImpersonationUserName пуст — используем ID клиента: ...`

**Это не ошибка, WARN.** Возникает, когда служба и клиент запущены под одной учёткой (например, тестируете `console` из-под админа).

В штатном режиме (служба под LocalSystem, клиент — обычный пользователь) этой строки быть не должно. Если она появляется — проверьте, не запущена ли служба под другой учёткой:

```bat
sc qc PaswordChService
:: Смотрите SERVICE_START_NAME — должно быть LocalSystem
```

---

### `Разрешена смена только своего пароля`

**Причина:** ваш логин не в списке `Managers`.

**Решение:** попросите администратора добавить вас:

```ini
[Access]
Managers=Администратор;test;ваш_логин
```

Затем перезапуск службы:
```bat
sc stop PaswordChService && sc start PaswordChService
```

Или используйте `run` под админом (без правки конфига):
```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" run test
```

---

### `Логин '<X>' защищён политикой безопасности`

**Причина:** логин в `ProtectedUsers`. Так задумано — никто не может его сменить.

**Решение:** если нужно разрешить смену — уберите логин из `ProtectedUsers` и перезапустите службу.

---

### `Ваш пароль защищён политикой безопасности`

**Причина:** вы — менеджер + защищённый (в обоих списках). Свой пароль менять нельзя.

**Решение:**
- поменять пароль средствами Windows (`Ctrl+Alt+Del`);
- или убрать себя из `ProtectedUsers`;
- или попросить другого менеджера/администратора.

---

### Письма не уходят

**Проверьте SMTP:**

```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" testmail
```

Частые причины:

| Ошибка | Причина | Решение |
|---|---|---|
| `Temporary local problem - please try later` | Rate limit SMTP | Уменьшить поток писем, использовать корпоративный SMTP |
| `Authentication failed` | Неверный логин/пароль | Для Yandex — «пароль приложения», не обычный |
| `Connection refused` | Порт/SSL неверные | Попробуйте 465 вместо 587 |
| Таймаут | Фаервол блокирует SMTP | Открыть исходящий на порт SMTP |

**Проверьте `SendEmailsToUsers`** — если `false`, письма пользователям не уходят (только отчёт админам).

---

### Логи пусты или пишутся не туда

Смотрите `Logs\service.log` **в папке с exe**:

```bat
dir /s /b "C:\Program Files\PaswordCh\Logs\service.log"
```

Если файл есть, но пуст — служба не смогла ничего записать. Проверьте права:

```bat
icacls "C:\Program Files\PaswordCh\Logs"
```

Должны быть `SYSTEM` и `Administrators` с `(F)`. Если нет:

```bat
icacls "C:\Program Files\PaswordCh\Logs" /grant "NT AUTHORITY\SYSTEM:(OI)(CI)F"
icacls "C:\Program Files\PaswordCh\Logs" /grant "BUILTIN\Administrators:(OI)(CI)F"
```

---

### `Файл emails.csv не найден:`

**Причина:** файл отсутствует или путь пустой.

Файл должен лежать рядом с `PaswordChService.exe`:

```
C:\Program Files\PaswordCh\emails.csv
```

Формат:
```
email;username
n.lobov@efsol.ru;n.lobov
```

Если файл по другому пути — укажите явно в `my.ini`:

```ini
[Paths]
CsvPath=D:\data\emails.csv
```

---

### `Канал нарушен` / `Pipe is broken`

**Это не ошибка.** Клиент отключился раньше, чем сервер закрыл поток. Логируется как `WARN` с пометкой «это нормально».

Если мешает — просто игнорируйте. Или уменьшите уровень логирования (см. `Logger.cs`).

---

### `Все экземпляры канала заняты`

**Причина:** пытаетесь запустить `console`, когда служба уже работает.

**Решение:**
```bat
sc stop PaswordChService
"C:\Program Files\PaswordCh\PaswordChService.exe" console
:: ...
sc start PaswordChService
```

---

### Пароли меняются при каждом перезапуске службы

**Причина:** устаревшая версия без счётчика `lastrun.txt`.

**Решение:** обновите бинарник. В новой версии `lastrun.txt` хранит дату последней смены, и цикл выполняется только когда прошло `IntervalDays` дней.

Проверить:
```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" nextrun
```

---

### `.NET Framework 4.8.1 не поддерживается этой операционной системой`

**Причина:** на Windows Server 2016 попытка поставить 4.8.1.

**Решение:** понизить целевую платформу до .NET Framework 4.8:

- Visual Studio: свойства проекта → Application → Target framework → `.NET Framework 4.8`.
- Или в `.csproj`: `<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>`.

Убедитесь, что на сервере установлен [.NET Framework 4.8 Runtime](https://dotnet.microsoft.com/download/dotnet-framework/net48).

---

### Антивирус блокирует

**Причина:** ложное срабатывание — программа меняет пароли и работает с AD, это поведение похоже на вредоносное.

**Решения:**

1. **Подписать код цифровым сертификатом** (Code Signing Certificate) — самый действенный способ.
2. **Добавить в исключения** антивируса на целевой машине: exe-файл, папку `C:\Program Files\PaswordCh\`, процесс.
3. **Отправить файл вендору антивируса** как false positive — они добавят в белый список.
4. **Запускать под встроенной учёткой Administrator** (не через UAC).
5. **Согласовать с ИТ-безопасностью** — в корпоративной среде централизованно добавить в whitelist.

---

## Логи: где искать

```
C:\Program Files\PaswordCh\Logs\service.log       — основной лог
C:\Program Files\PaswordCh\Logs\lastrun.txt       — дата последней смены
C:\Program Files\PaswordCh\Archive\*.zip          — зашифрованные архивы
C:\Program Files\PaswordCh\ErrorUsers\*.csv       — пользователи с ошибками
```

EventLog: Просмотр событий → Журналы Windows → Application → источник `PaswordChService`.

---

← [Назад к README](../README.md) · [← Безопасность](SECURITY.md) · [Далее: Changelog →](CHANGELOG.md)