# PaswordChService

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-blue.svg)](https://dotnet.microsoft.com/download/dotnet-framework/net48)
[![Platform](https://img.shields.io/badge/platform-Windows%20Server%202016%2B-lightgrey.svg)](#системные-требования)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
---

## Зачем этот проект

Пароли — самое слабое звено в ИТ-инфраструктуре и одновременно самая рутинная задача для системного администратора.

Сотрудники используют одни и те же пароли годами. После увольнения администратора или подозрения на утечку приходится вручную менять пароли десяткам учётных записей. Пользователи не могут сменить свой пароль без обращения в техподдержку. Аудит смены паролей отсутствует, а сами пароли передаются по телефону или на стикере.

**PaswordChService** решает эти задачи одной службой Windows: периодически меняет пароли по расписанию, уведомляет пользователей и администраторов, хранит пароли в зашифрованном виде и разграничивает доступ по ролям.

---

## Что внутри

- **Автоматическая смена паролей** раз в N дней — без участия администратора.
- **Три роли:** обычный пользователь (только свой пароль), менеджер (чужие), защищённый логин (нельзя менять никому).
- **Смена без прав администратора** — через именованный канал, служба работает под `LocalSystem`.
- **HTML-письма** в фирменном стиле — пользователю с новым паролем, администраторам с отчётом.
- **AES-256 ZIP-архивы** с новыми паролями.
- **Шифрование секретов** `my.ini` через Windows DPAPI — `ZipPassword`, `SmtpPassword`, `AdAdminPassword`.
- **Тройной аудит:** файловый лог, журнал событий Windows, консоль в режиме отладки.
- **Единая установка:** один `.exe`, один `.bat`, один `.ini`.

---

## Для кого

- **Системные администраторы** — автоматизировать ротацию паролей.
- **ИТ-отделы компаний** — централизованно управлять паролями локальных учёток и Active Directory.

---

## Что дальше

Полная документация — установка, конфигурация, команды, архитектура, безопасность  и деагностика — в основном файле:

### → [PaswordChService/README.md](PaswordChService/README.md)

Быстрые ссылки на разделы документации:

| Раздел | Документ |
|---|---|
| Установка и миграция | [PaswordChService/docs/INSTALL.md](PaswordChService/docs/INSTALL.md) |
| Справочник `my.ini` | [PaswordChService/docs/CONFIG.md](PaswordChService/docs/CONFIG.md) |
| Все CLI-команды | [PaswordChService/docs/COMMANDS.md](PaswordChService/docs/COMMANDS.md) |
| Архитектура и pipe | [PaswordChService/docs/ARCHITECTURE.md](PaswordChService/docs/ARCHITECTURE.md) |
| Роли и шифрование | [PaswordChService/docs/SECURITY.md](PaswordChService/docs/SECURITY.md) |
| Диагностика | [PaswordChService/docs/TROUBLESHOOTING.md](PaswordChService/docs/TROUBLESHOOTING.md) |
| История версий | [PaswordChService/docs/CHANGELOG.md](PaswordChService/docs/CHANGELOG.md) |

---

- **Issues:** через GitHub Issues репозитория
