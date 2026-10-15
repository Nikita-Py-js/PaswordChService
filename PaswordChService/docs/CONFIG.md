# Конфигурация `my.ini`

← [Назад к README](../README.md) · [← Установка](INSTALL.md) · [Далее: Команды →](COMMANDS.md)

---

## Общие правила

- Файл читается при старте службы. После правки — перезапуск: `sc stop PaswordChService && sc start PaswordChService`.
- Поддерживаются кодировки UTF-8 (с/без BOM), UTF-16 LE/BE, Windows-1251.
- Комментарии — `;` или `#` в начале строки.
- Разделитель списков — `;` или `,`.

---

## `[Branding]`

Оформление писем и подписи.

| Ключ | По умолчанию | Описание |
|---|---|---|
| `CompanyBrandHtml` | `<span>EFSOL</span>` | HTML-логотип в шапке. Вставляется без экранирования |
| `CompanyName` | `EFSOL` | Название компании (текстом) |
| `ClientCompanyName` | (пусто) | Компания-клиент. Fallback на `CompanyName` |
| `CompanyTagline` | (пусто) | Слоган в шапке |
| `CompanySite` | (пусто) | Сайт в подвале |
| `CompanyEmail` | (пусто) | Email в подвале |
| `CompanyPhone` | (пусто) | Телефон в подвале |
| `VendorLine` | (пусто) | Строка вендора |
| `VendorUrl` | (пусто) | URL вендора |
| `EmailUserSubject` | `Смена пароля` | Тема письма пользователю |
| `EmailAdminSubject` | `PaswordCh: отчёт о смене паролей` | Тема отчёта админам |

### Плейсхолдеры в темах

**В `EmailUserSubject`:**
- `{{UserName}}` — логин
- `{{ClientCompanyName}}` — компания клиента
- `{{CompanyName}}` — наша компания

**В `EmailAdminSubject`:**
- `{{ClientCompanyName}}`, `{{CompanyName}}`
- `{{TotalSuccess}}` — успешно
- `{{TotalErrors}}` — ошибок

Пример:
```ini
EmailUserSubject=[{{CompanyName}}] Смена пароля для {{UserName}} — {{ClientCompanyName}}
EmailAdminSubject=[{{CompanyName}}] Отчёт: {{TotalSuccess}} успешно / {{TotalErrors}} ошибок
```

### Примеры `CompanyBrandHtml`

```ini
; Двухцветный текст
CompanyBrandHtml=<span style="color:#ffffff;">EF</span><span style="color:#ffcc00;">SOL</span>

; Один цвет
CompanyBrandHtml=<span style="color:#ffcc00;font-weight:900;">EFSOL</span>

; Картинка
CompanyBrandHtml=<img src="https://efsol.ru/logo.png" alt="EFSOL" height="28" style="display:block;border:0;">
```

---

## `[Service]`

| Ключ | По умолчанию | Описание |
|---|---|---|
| `IntervalDays` | `30` | Смена паролей раз в N дней |
| `CheckIntervalMinutes` | `15` | Как часто проверять счётчик |
| `LogArchiveHours` | `24` | Ротация `service.log` раз в N часов |

Счётчик последней смены хранится в `Logs\lastrun.txt`. Перезапуск службы его не сбрасывает.

---

## `[Users]`

| Ключ | По умолчанию | Описание |
|---|---|---|
| `Mode` | `Local` | `Local` — локальные учётки, `AD` — Active Directory |
| `Domain` | (пусто) | Домен для `Mode=AD` |
| `AdAdminUser` | (пусто) | Доменная учётка с правами Reset Password |
| `AdAdminPassword` | (пусто) | Пароль этой учётки (шифруется) |
| `PasswordLength` | `12` | Длина генерируемого пароля (мин. 8) |

Пароли генерируются `RNGCryptoServiceProvider`, всегда содержат: заглавные, строчные, цифры, спецсимволы.

---

## `[Archive]`

| Ключ | По умолчанию | Описание |
|---|---|---|
| `ZipPassword` | `ChangeMe` | Пароль для AES-256-шифрования ZIP |

**Обязательно сменить!** Пароль хранится зашифрованным через DPAPI.

---

## `[Mail]`

| Ключ | По умолчанию | Описание |
|---|---|---|
| `AdminEmails` | (пусто) | Список админов через `;` или `,` |
| `SmtpHost` | (пусто) | SMTP-сервер |
| `SmtpPort` | `25` | Порт (25, 465, 587) |
| `SmtpUseSsl` | `false` | SSL/TLS |
| `SmtpUser` | (пусто) | Логин SMTP |
| `SmtpPassword` | (пусто) | Пароль SMTP (шифруется) |
| `SmtpFrom` | (пусто) | Адрес отправителя |
| `SendEmailsToUsers` | `false` | Слать ли письма самим пользователям |
| `SmtpTimeoutMs` | `30000` | Таймаут SMTP |

### Пример Yandex
```ini
SmtpHost=smtp.yandex.ru
SmtpPort=465
SmtpUseSsl=true
SmtpUser=n.lobov57@yandex.ru
SmtpPassword=<пароль приложения>
SmtpFrom=n.lobov57@yandex.ru
```

### Пример Reg.ru
```ini
SmtpHost=mail.hosting.reg.ru
SmtpPort=587
SmtpUseSsl=true
SmtpUser=service@reument.ru
SmtpPassword=<пароль>
SmtpFrom=service@reument.ru
```

---

## `[Access]`

| Ключ | Описание |
|---|---|
| `Managers` | Логины с ролью менеджера (меняют чужие пароли) |
| `ProtectedUsers` | Логины, чей пароль менять нельзя никому |

Разделитель — `;` или `,`.

Пример:
```ini
Managers=Администратор;test;efsol_dc
ProtectedUsers=Administrator;efsol_it;efsol_dc
```

Логин **может быть в обоих списках** — тогда он менеджер, но его собственный пароль защищён. Так настроен `efsol_dc` в примере.

Подробнее о ролях — [SECURITY.md](SECURITY.md).

---

## `[Paths]` (опционально)

Если секции нет — используются пути из `Paths.cs`:

| Ключ | Дефолт |
|---|---|
| `CsvPath` | `C:\Program Files\PaswordCh\emails.csv` |
| `ArchivePath` | `C:\Program Files\PaswordCh\Archive` |
| `LogPath` | `C:\Program Files\PaswordCh\Logs` |
| `ErrorUsersPath` | `C:\Program Files\PaswordCh\ErrorUsers` |

Секцию добавляют только если нужно вынести данные в другое место:

```ini
[Paths]
ArchivePath=D:\PaswordChBackup\Archive
```

---

## Шифрование секретов

Три параметра шифруются через Windows DPAPI (`LocalMachine`):

- `AdAdminPassword`
- `ZipPassword`
- `SmtpPassword`

### Как это работает

1. Указываете открытое значение: `ZipPassword=MySecret123`
2. Служба при старте шифрует его и записывает обратно: `ZipPassword=ENC:AQAAANCMnd8BFdE...`
3. При чтении значение автоматически расшифровывается.

### Ручное управление

```bat
:: Зашифровать все открытые секреты
PaswordChService.exe seal

:: Расшифровать обратно (с подтверждением)
PaswordChService.exe unseal

:: Одно значение
PaswordChService.exe encrypt MyPassword
PaswordChService.exe decrypt ENC:AQAAANCMnd8BFdE...
```

### Важно

- Значения `ENC:` **привязаны к машине**. На другом ПК расшифровать нельзя.
- При переносе на другой сервер — сначала `unseal`, потом перенос, потом `seal` на новом.
- Если хотите вообще без шифрования — оставляйте пароли открытыми. Программа работает с ними так же.

---

← [Назад к README](../README.md) · [← Установка](INSTALL.md) · [Далее: Команды →](COMMANDS.md)
