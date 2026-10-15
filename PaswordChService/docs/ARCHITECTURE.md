# Архитектура

← [Назад к README](../README.md) · [← Команды](COMMANDS.md) · [Далее: Безопасность →](SECURITY.md)

---

## Компоненты

```
┌─────────────────────────────────────────────────────────────┐
│  PaswordChService.exe                                       │
│                                                             │
│  ┌────────────┐   ┌──────────────┐   ┌──────────────────┐   │
│  │  Host      │   │  Service     │   │  PipeServer      │   │
│  │  (CLI)     │   │  (Program)   │   │  (named pipe)    │   │
│  └──────┬─────┘   └──────┬───────┘   └────────┬─────────┘   │
│         │                │                     │            │
│         └────────────────┴─────────────────────┘            │
│                          │                                  │
│                ┌─────────▼──────────┐                       │
│                │  EventProcessor    │                       │
│                │  (бизнес-логика)   │                       │
│                └──┬───────┬──────┬──┘                       │
│                   │       │      │                          │
│       ┌───────────┘       │      └──────────┐               │
│       ▼                   ▼                 ▼               │
│  ┌──────────┐       ┌──────────┐      ┌──────────┐          │
│  │ Password │       │  Email   │      │   Zip    │          │
│  │ Manager  │       │  Sender  │      │ Archiver │          │
│  └──────────┘       └──────────┘      └──────────┘          │
│                                                             │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │  Logger  │  │  Config  │  │  Crypto  │  │  INI     │    │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘    │
└─────────────────────────────────────────────────────────────┘
```

---

## Файлы проекта

| Файл | Роль |
|---|---|
| `Program.cs` | Класс службы `PaswordChService : ServiceBase`. Старт/стоп, цикл `WorkerLoop`, счётчик `lastrun.txt`. |
| `Host.cs` | Точка входа + CLI-команды (install, change, run, seal, console, ...). |
| `PipeServer.cs` | Named pipe сервер. Принимает запросы `change`/`run` от непривилегированных клиентов. |
| `EventProcessor.cs` | Бизнес-логика цикла: чтение CSV, генерация паролей, параллельная обработка, сохранение результатов. |
| `PasswordManager.cs` | Смена пароля в SAM (Local) или AD. |
| `EmailSender.cs` | SMTP-отправка HTML-писем с retry и троттлингом. |
| `EmailTemplate.cs` | Загрузка HTML-шаблонов и подстановка `{{Placeholder}}`. |
| `ZipArchiver.cs` | Упаковка результатов в ZIP с AES-256-шифрованием. |
| `Logger.cs` | Логирование в файл + EventLog. |
| `ServiceConfig.cs` | Чтение и валидация `my.ini`. Шифрование секретов. |
| `INIManager.cs` | Парсер INI с поддержкой всех кодировок. |
| `CryptoHelper.cs` | Windows DPAPI обёртка (`LocalMachine`). |
| `Paths.cs` | Единая точка для всех путей. |
| `ProjectInstaller.cs` | Установщик службы (`ManagedInstallerClass`). |

---

## Жизненный цикл службы

```
sc start PaswordChService
        │
        ▼
┌────────────────────────┐
│  OnStart()             │
│                        │
│  1. Paths.EnsureDataDirs()         │
│  2. Logger.Initialize(Paths.LogsDir)│
│  3. SealSecrets(my.ini)            │ ◄── автошифрование открытых паролей
│  4. ServiceConfig.Load(my.ini)     │
│  5. Запуск WorkerLoop (поток)      │
│  6. Запуск PipeServer (поток)      │
└────────────────────────┘
        │
        ▼
┌────────────────────────┐
│  WorkerLoop            │
│                        │
│  while (!stopped) {    │
│    if (IsTimeToRun())  │  ◄── проверка lastrun.txt + IntervalDays
│      RunOnce();        │
│    SaveLastRun();      │
│    RotateLogs();       │
│    Sleep(CheckInterval)│
│  }                     │
└────────────────────────┘
```

`OnStop` — устанавливает событие остановки, ждёт завершения потока (до 30 с), останавливает pipe.

---

## Цикл `RunOnce`

```
1. Чтение emails.csv → Dictionary<username, email>
2. Фильтрация:
   - в полном цикле: пропуск защищённых логинов
   - в выборочном: только указанные
3. Для каждого пользователя (параллельно через Task.Run):
   a. Сгенерировать криптостойкий пароль
   b. PasswordManager.ChangePassword()
   c. Если успех и SendEmailsToUsers — EmailSender.SendUserPassword()
4. Сохранить успешные результаты в CSV
5. Упаковать CSV в AES-256 ZIP, удалить открытый CSV
6. Сохранить ошибки в ErrorUsers/errors_*.csv
7. В полном цикле — отчёт админам (HTML)
```

Все задачи выполняются параллельно (`Task.WaitAll` с таймаутом 30 минут). Ошибка на одном пользователе не блокирует остальных.

---

## Named pipe протокол

Формат сообщений — построчный текст UTF-8.

### Идентификация (первое сообщение от клиента)

```
ID|<DOMAIN\user>
```

Служба:
1. Пытается получить имя клиента через `pipe.GetImpersonationUserName()`.
2. Если имя получено — **сверяет с заявленным**. Несовпадение → `ERR|Несоответствие идентификации`.
3. Если пусто (debug-сценарий) — доверяет заявленному с записью в `WARN`.

### Команды

| Команда | Ответ |
|---|---|
| `PING` | `PONG` |
| `CHANGE\|<user>` | `OK\|<новый пароль>` или `ERR\|<текст>` |
| `RUN\|<u1;u2>` | Многострочный: `OK\|user\|email\|password` / `ERR\|...` / `FATAL\|...` / `DONE` |
| `QUIT` | закрытие |

### Пример сессии

```
C → ID|REUMENT-DEV-NOD\Администратор
C → CHANGE|test
S → OK|Kd8#nQp3Wm2x
C → QUIT
```

---

## Счётчик дней

Хранится в `Logs\lastrun.txt` — одна строка ISO-8601:

```
2026-10-15T11:20:08.1234567
```

Проверка перед циклом:
```csharp
if (last == DateTime.MinValue) return true;  // первый запуск
return (DateTime.Now - last).TotalDays >= IntervalDays;
```

После успешного цикла дата перезаписывается. Перезапуск службы не влияет на счётчик.

Сбросить принудительно — удалить `lastrun.txt` и перезапустить службу. Или отредактировать дату вручную.

---

## Разделение процессов

`PaswordChService.exe` работает в двух режимах:

**Режим службы** — процесс запущен SCM'ом, без интерактивной консоли (`GetConsoleWindow() == IntPtr.Zero`):
- запускает `ServiceBase.Run(new PaswordChService())`;
- работает под `LocalSystem`;
- доступ к pipe — `AuthenticatedUsers`.

**Режим CLI** — процесс запущен из cmd:
- обрабатывает команды (`change`, `run`, ...);
- работает под учётной записью пользователя;
- подключается к pipe как клиент.

Один и тот же EXE — разные ветки поведения в `Host.Main`.

---

← [Назад к README](../README.md) · [← Команды](COMMANDS.md) · [Далее: Безопасность →](SECURITY.md)