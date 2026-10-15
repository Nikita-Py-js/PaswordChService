# PaswordChService

Windows-служба автоматической смены паролей пользователей (локальных или Active Directory) с email-уведомлениями, шифрованным архивированием и ролевым доступом.

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework/net48)
[![Platform](https://img.shields.io/badge/platform-Windows%20Server%202016%2B-lightgrey.svg)](#системные-требования)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## Что это

`PaswordChService` — фоновая служба Windows, которая:

- **меняет пароли пользователей раз в N дней** — автоматически, по счётчику;
- **работает без прав администратора** для повседневных операций (через именованный канал);
- **разграничивает доступ** — обычный пользователь, менеджер, защищённый логин;
- **уведомляет** пользователей и администраторов красивыми HTML-письмами;
- **шифрует** новые пароли в AES-256 ZIP-архивах;
- **логирует** всё в файл, EventLog и консоль;
- **хранит секреты** (`ZipPassword`, `SmtpPassword`, `AdAdminPassword`) зашифрованными через Windows DPAPI.

Подходит для администраторов, которым нужно автоматизировать ротацию паролей без покупки дорогих решений вроде ManageEngine или Specops.

---

## Быстрый старт

```bat
:: 1. Скопировать файлы из релиза в C:\Program Files\PaswordCh\
:: 2. Отредактировать my.ini (SMTP, роли, пути)
:: 3. Установить службу (от админа)
ServiceInstall.bat

:: 4. Проверить работу
"C:\Program Files\PaswordCh\PaswordChService.exe" ping
"C:\Program Files\PaswordCh\PaswordChService.exe" whoami
"C:\Program Files\PaswordCh\PaswordChService.exe" testmail
```

Полная инструкция — [docs/INSTALL.md](docs/INSTALL.md).

---

## Возможности

| Возможность | Описание |
|---|---|
| **Периодическая смена** | Раз в N дней, счётчик в `lastrun.txt` |
| **Смена по запросу** | `change`, `run`, `runall` |
| **Local + AD** | Оба режима поддерживаются из коробки |
| **Роли** | Обычный / Менеджер / Защищённый |
| **Без прав админа** | Через named pipe, служба делает всё под SYSTEM |
| **HTML-письма** | Настраиваемый брендинг, темы, подписи |
| **AES-256 ZIP** | Шифрование архива с новыми паролями |
| **DPAPI для секретов** | Пароли SMTP/ZIP/AD зашифрованы в `my.ini` |
| **Мультилогирование** | Файл, EventLog, консоль |
| **Автозапуск и restart** | Через SCM, с `sc failure` для восстановления |

---

## Документация

| Документ | Что внутри |
|---|---|
| [Установка](docs/INSTALL.md) | Системные требования, установка, миграция, деинсталляция |
| [Конфигурация](docs/CONFIG.md) | Полный разбор `my.ini` — все параметры, примеры |
| [Команды](docs/COMMANDS.md) | CLI-справочник: `change`, `run`, `whoami`, `seal`, `console`, ... |
| [Архитектура](docs/ARCHITECTURE.md) | Как устроено внутри: pipe, служба, обработчик цикла |
| [Безопасность](docs/SECURITY.md) | Роли, права, шифрование, модель угроз |
| [Troubleshooting](docs/TROUBLESHOOTING.md) | FAQ, частые ошибки, диагностика |
| [Changelog](docs/CHANGELOG.md) | История версий |

---

## Системные требования

- **ОС:** Windows Server 2016 / 2019 / 2022 или Windows 10 / 11
- **.NET Framework 4.8** (входит в состав Windows 10 1903+ и Windows Server 2019+)
- **Права:** администратор Windows — для установки службы; обычный пользователь — для повседневного использования `change`
- **Диск:** ~5 МБ для бинарника + место под логи и архивы

---

## Архитектура в двух словах

```
┌────────────────────┐              ┌────────────────────────────┐
│  Пользователь      │              │  PaswordChService          │
│  (без прав админа) │  named pipe  │  (LocalSystem)             │
│                    ├──────────────►                            │
│  PaswordCh.exe     │  CHANGE|user │  Pipe-сервер → EventProc → │
│  change test       │◄──────────────┤  PasswordManager           │
│                    │  OK|пароль   │                            │
└────────────────────┘              └────────────────────────────┘
                                             │
                                             ├─► AD / Local SAM
                                             ├─► SMTP (письма)
                                             ├─► AES ZIP (архив)
                                             └─► Logs / EventLog
```

Подробнее — [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Лицензия

MIT. См. файл [LICENSE](LICENSE).
