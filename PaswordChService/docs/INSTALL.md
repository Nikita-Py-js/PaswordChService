# Установка

← [Назад к README](../README.md) · [Далее: Конфигурация →](CONFIG.md)

---

## Системные требования

| Компонент | Требование |
|---|---|
| ОС | Windows Server 2016 / 2019 / 2022, Windows 10 / 11 |
| Платформа | .NET Framework 4.8 (входит в состав Win10 1903+, WinServer 2019+) |
| Права | Администратор — для установки, обычный пользователь — для `change` |
| Место | ~5 МБ (exe + html) + место под логи и архивы |

> **Windows Server 2016**: .NET Framework 4.8 нужно установить вручную (скачать [здесь](https://dotnet.microsoft.com/download/dotnet-framework/net48)). Версии 4.8.1 и выше на Server 2016 не поддерживаются.

---

## Установка

### 1. Подготовка

Скачайте архив с релизом и распакуйте во временный каталог:

```
PaswordChService.exe
PaswordChService.exe.config
EmailUser.html
EmailAdmin.html
my.ini
emails.csv
ServiceInstall.bat
ServiceUninstall.bat
```

### 2. Редактирование `my.ini`

Откройте `my.ini` в блокноте и заполните обязательные поля:

| Параметр | Что указать |
|---|---|
| `Mode` | `Local` или `AD` |
| `Domain` | Домен (только для `Mode=AD`) |
| `AdAdminUser` / `AdAdminPassword` | Доменная учётка с правами Reset Password |
| `ZipPassword` | Пароль для шифрования ZIP-архива |
| `AdminEmails` | Кому слать отчёт |
| `SmtpHost`, `SmtpPort`, `SmtpUser`, `SmtpPassword`, `SmtpFrom` | Параметры SMTP |
| `Managers` | Логины менеджеров (могут менять чужие пароли) |
| `ProtectedUsers` | Логины, чей пароль менять нельзя никому |

Полный разбор всех параметров — [CONFIG.md](CONFIG.md).

### 3. Файл `emails.csv`

Формат:
```
email;username
```

Пример:
```
n.lobov@efsol.ru;n.lobov
test@efsol.ru;test
```

Строки, начинающиеся с `#`, игнорируются. Если пользователь есть в CSV — ему уйдёт письмо; если нет — пароль сменится, письмо не отправится.

### 4. Установка службы (от админа)

Правый клик на `ServiceInstall.bat` → **Запуск от имени администратора**.

Скрипт:
- скопирует файлы в `C:\Program Files\PaswordCh\`
- создаст каталоги `Logs`, `Archive`, `ErrorUsers`
- скопирует `my.ini` и `emails.csv` (если их нет)
- установит службу с типом «Автоматически»
- настроит перезапуск при сбоях
- запустит службу

Ожидаемый вывод:
```
=== 1. Копирование файлов в C:\Program Files\PaswordCh\ ===
=== 2. Создание каталогов ===
=== 3. Установка службы ===
Служба успешно установлена.
=== 4. Запуск службы ===
Служба запущена.
```

### 5. Проверка

```bat
:: Состояние службы
sc query PaswordChService

:: Связь со службой
"C:\Program Files\PaswordCh\PaswordChService.exe" ping
:: Ответ службы: PONG

:: Проверка конфига
"C:\Program Files\PaswordCh\PaswordChService.exe" whoami
"C:\Program Files\PaswordCh\PaswordChService.exe" nextrun

:: Отправка тестового письма
"C:\Program Files\PaswordCh\PaswordChService.exe" testmail
```

Лог:
```
C:\Program Files\PaswordCh\Logs\service.log
```

EventLog: **Просмотр событий → Журналы Windows → Приложение → источник `PaswordChService`**.

---

## Миграция с `C:\PaswordCh\`

Если у вас была установлена предыдущая версия (данные в `C:\PaswordCh\`):

```bat
:: От админа. Сначала расшифровать секреты, если планируете перенос на другой сервер.
C:\PaswordCh\PaswordChService.exe unseal

:: Скопировать данные
xcopy /E /I /Y "C:\PaswordCh\my.ini"       "C:\Program Files\PaswordCh\"
xcopy /E /I /Y "C:\PaswordCh\emails.csv"   "C:\Program Files\PaswordCh\"
xcopy /E /I /Y "C:\PaswordCh\Logs\*"       "C:\Program Files\PaswordCh\Logs\"
xcopy /E /I /Y "C:\PaswordCh\Archive\*"    "C:\Program Files\PaswordCh\Archive\"
xcopy /E /I /Y "C:\PaswordCh\ErrorUsers\*" "C:\Program Files\PaswordCh\ErrorUsers\"

:: Удалить старую службу
C:\PaswordCh\PaswordChService.exe uninstall

:: Установить новую
ServiceInstall.bat

:: Удалить старый каталог
rmdir /S /Q C:\PaswordCh
```

При первом запуске служба автоматически зашифрует открытые пароли в новом `my.ini`.

---

## Обновление бинарника

```bat
sc stop PaswordChService
copy /Y PaswordChService.exe "C:\Program Files\PaswordCh\"
sc start PaswordChService
```

Файлы `my.ini`, `emails.csv`, `Logs\`, `Archive\` не трогаются.

Если в новой версии появились новые HTML-шаблоны — обновите и их:
```bat
copy /Y EmailUser.html  "C:\Program Files\PaswordCh\"
copy /Y EmailAdmin.html "C:\Program Files\PaswordCh\"
```

---

## Удаление

Правый клик на `ServiceUninstall.bat` → **Запуск от имени администратора**.

Скрипт:
- остановит службу;
- удалит её из SCM;
- спросит про удаление каталога программы и каталога данных.

Ручное удаление:
```bat
sc stop PaswordChService
"C:\Program Files\PaswordCh\PaswordChService.exe" uninstall
rmdir /S /Q "C:\Program Files\PaswordCh"
```

---

## Структура после установки

```
C:\Program Files\PaswordCh\
├── PaswordChService.exe
├── PaswordChService.exe.config
├── EmailUser.html
├── EmailAdmin.html
├── my.ini
├── emails.csv
├── Logs\
│   ├── service.log
│   └── lastrun.txt
├── Archive\
│   └── newpasswords_YYYYMMDD_HHMMSS.zip
└── ErrorUsers\
    └── errors_YYYYMMDD_HHMMSS.csv
```

---

← [Назад к README](../README.md) · [Далее: Конфигурация →](CONFIG.md)