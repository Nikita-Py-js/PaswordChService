# Создание своего HTML-шаблона

← [Назад к README](../README.md) · [← Конфигурация](CONFIG.md) · [Далее: Безопасность →](SECURITY.md)

---

## Что это

Письма, которые отправляет служба, формируются из двух HTML-файлов:

- `EmailUser.html` — письмо пользователю с новым паролем.
- `EmailAdmin.html` — отчёт администраторам о цикле.

Оба лежат рядом с `PaswordChService.exe`:

```
C:\Program Files\PaswordCh\
├── PaswordChService.exe
├── EmailUser.html
└── EmailAdmin.html
```

Файлы можно полностью заменить на свои. Внутри используются **плейсхолдеры** вида `{{UserName}}` — служба подставляет в них значения при отправке.

---

## Приоритет поиска шаблонов

Служба ищет файл в двух местах по порядку:

1. `C:\Program Files\PaswordCh\<файл>.html` — основной путь.
2. Если файл не найден — ошибка, отправляется простое текстовое письмо.

Чтобы изменить шаблон:

1. Отредактируйте файл в `C:\Program Files\PaswordCh\`.
2. Перезапустите службу: `sc stop PaswordChService && sc start PaswordChService`.

---

## Список плейсхолдеров

### `EmailUser.html` — письмо пользователю

| Плейсхолдер | Что подставляет |
|---|---|
| `{{Greeting}}` | Приветствие с логином: «Здравствуйте, test!» |
| `{{UserName}}` | Логин |
| `{{NewPassword}}` | Новый пароль |
| `{{Date}}` | Дата и время смены (`dd.MM.yyyy HH:mm`) |
| `{{CompanyBrandHtml}}` / `{{BrandHtml}}` | HTML-логотип из `my.ini` (вставляется без экранирования) |
| `{{CompanyName}}` | Название компании |
| `{{ClientCompanyName}}` | Компания-клиент (fallback — `CompanyName`) |
| `{{CompanyTagline}}` | Слоган |
| `{{CompanySite}}` | URL сайта |
| `{{CompanySiteUrl}}` | URL сайта (нормализованный, с `https://`) |
| `{{CompanyEmail}}` | Email |
| `{{CompanyPhone}}` | Телефон |
| `{{VendorLine}}` | Строка вендора |
| `{{VendorUrl}}` | URL вендора (нормализованный) |

### `EmailAdmin.html` — отчёт администраторам

Все плейсхолдеры из таблицы выше, **плюс**:

| Плейсхолдер | Что подставляет |
|---|---|
| `{{TotalSuccess}}` | Количество успешных смен |
| `{{TotalErrors}}` | Количество ошибок |
| `{{NextRun}}` | Дата следующей смены |
| `{{Rows}}` | Готовые строки таблицы `<tr>...</tr>` с результатами |
| `{{ArchivePath}}` | Путь к каталогу архивов |
| `{{LogPath}}` | Путь к каталогу логов |

**Плейсхолдер `{{Rows}}`** — особый. Служба сама формирует HTML-строки `<tr>` для таблицы результатов и подставляет их как есть. В шаблоне нужно только правильно обернуть его в `<table><tbody>...</tbody></table>`.

---

## Минимальный шаблон

Работает, если не хочется возиться с вёрсткой:

```html
<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <title>Смена пароля</title>
</head>
<body style="font-family: Arial, sans-serif; padding: 20px;">

  <h1>Смена пароля</h1>

  <p>{{Greeting}}</p>

  <p>Для вашей учётной записи <b>{{UserName}}</b> был установлен новый пароль:</p>

  <p style="font-size: 24px; font-family: monospace; background: #f0f0f0; padding: 12px;">
    {{NewPassword}}
  </p>

  <p>Дата смены: {{Date}}</p>

  <p>Сохраните пароль в надёжном месте.</p>

  <hr>
  <p style="font-size: 12px; color: #888;">
    {{CompanyName}} · {{CompanySite}} · {{CompanyEmail}}
  </p>

</body>
</html>
```

---

## Требования к письму

### 1. Обязательный DOCTYPE и `<meta charset>`

```html
<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  ...
</head>
```

Без `charset=utf-8` кириллица в письме может превратиться в кракозябры.

### 2. Инлайн-стили, не CSS-классы

Почтовые клиенты (Outlook, Gmail, Yandex.Mail) **не поддерживают** `<style>`-блоки и внешние CSS. Все стили должны быть inline:

**Плохо:**
```html
<style>
  .password { font-family: monospace; font-size: 24px; }
</style>

<div class="password">{{NewPassword}}</div>
```

**Хорошо:**
```html
<div style="font-family: monospace; font-size: 24px;">{{NewPassword}}</div>
```

### 3. Вёрстка таблицами, не div/flex/grid

Outlook использует движок Word для рендеринга и не понимает современные CSS-свойства. Карточки и колонки собираются через `<table>`:

```html
<table role="presentation" width="100%" cellpadding="0" cellspacing="0">
  <tr>
    <td align="center" style="padding: 20px;">
      <!-- содержимое -->
    </td>
  </tr>
</table>
```

Атрибут `role="presentation"` говорит скринридерам, что это не структурная таблица, а визуальная.

### 4. Не использовать JavaScript

Почтовые клиенты **вырезают** `<script>`. Ничего интерактивного в письмах быть не может.

### 5. Не использовать `<form>` и `<input>`

Формы в письмах обычно вырезаются или блокируются. Если нужны кнопки — только ссылки `<a>`, стилизованные как кнопки.

### 6. Не использовать внешние ресурсы без HTTPS

Если вставляете логотип с сервера — только HTTPS. Иначе Gmail/Outlook могут заблокировать картинку или показать предупреждение.

```html
<img src="https://efsol.ru/logo.png" alt="EFSOL" height="28" style="display:block;border:0;">
```

Параметр `height` лучше указывать явно — некоторые клиенты иначе рендерят картинку в натуральную величину и ломают вёрстку.

### 7. Кнопки — как ссылки

Правильная «кнопка» в письме:

```html
<table role="presentation" cellpadding="0" cellspacing="0">
  <tr>
    <td style="background:#ffc400;border-radius:4px;">
      <a href="https://efsol.ru" target="_blank"
         style="display:inline-block;padding:12px 24px;font-weight:700;color:#000;text-decoration:none;">
        Перейти на сайт
      </a>
    </td>
  </tr>
</table>
```

---

## Пошаговый пример: свой шаблон

Задача — сделать минималистичное письмо с белым фоном, чёрным текстом и оранжевой полосой сверху.

### 1. Откройте `EmailUser.html`

```bat
notepad "C:\Program Files\PaswordCh\EmailUser.html"
```

### 2. Замените содержимое

html


### 3. Сохраните файл **в UTF-8 без BOM**

Блокнот: **Файл → Сохранить как → Кодировка: UTF-8**.

Notepad++: **Кодировки → Преобразовать в UTF-8 без BOM**.

### 4. Перезапустите службу

```bat
sc stop PaswordChService
sc start PaswordChService
```

### 5. Проверьте

```bat
"C:\Program Files\PaswordCh\PaswordChService.exe" run test
```

Откройте почту (или `testmail`) — увидите своё письмо.

---

## Проверка шаблона до отправки

Перед тем как рассылать пользователям, проверьте шаблон локально:

1. **Откройте HTML-файл в браузере.** Замените плейсхолдеры на тестовые значения вручную:

   ```
   {{Greeting}}    → Здравствуйте, test!
   {{UserName}}    → test
   {{NewPassword}} → Abc123!@#
   {{Date}}        → 15.10.2026 11:20
   ```

   Сохраните как `test.html` и откройте в Chrome/Edge/Firefox — увидите, как будет выглядеть в большинстве почтовых клиентов.

2. **Отправьте тестовое письмо на свой адрес:**

   ```bat
   "C:\Program Files\PaswordCh\PaswordChService.exe" testmail your@email.com
   ```

   `testmail` отправляет простое текстовое письмо, чтобы проверить SMTP. Для проверки HTML-шаблона используйте `run` на тестового пользователя — тогда уйдёт настоящее HTML-письмо.

3. **Проверьте в разных клиентах.** Одна и та же вёрстка может выглядеть по-разному:
   - Outlook (Windows) — самый капризный, движок Word.
   - Gmail (веб) — вырезает `<style>` и часть CSS-свойств.
   - Yandex.Mail — примерно как Gmail.
   - Мобильные клиенты — упрощённый рендеринг.

   Минимум — протестировать в Outlook и Gmail.

---

## Где взять готовые шаблоны

- **Стандартные шаблоны проекта** — `EmailUser.html` и `EmailAdmin.html` в поставке. Их можно взять за основу.
- **Общедоступные шаблоны** — [litmus.com](https://litmus.com/community/templates), [mailchimp.com](https://mailchimp.com/resources/email-templates/) (бесплатные).
- **Своя вёрстка** — не забудьте проверить в Outlook и Gmail перед боевым запуском.

---

## Частые ошибки

| Симптом | Причина | Решение |
|---|---|---|
| Кракозябры вместо кириллицы | Файл сохранён без UTF-8 | Пересохранить в UTF-8 без BOM |
| Плейсхолдер виден как `{{UserName}}` | Опечатка в имени | Проверить регистр: `{{UserName}}`, не `{{username}}` |
| Письмо приходит текстом | Шаблон не найден | Проверить путь `C:\Program Files\PaswordCh\EmailUser.html` |
| Вёрстка поехала в Outlook | Использованы `div` + flex/grid | Переверстать на `<table>` |
| Пустой квадрат вместо логотипа | Картинка не по HTTPS или заблокирована | Использовать HTTPS, добавить `alt` |
| Стили не применились | Использованы CSS-классы | Переписать на inline-стили |
| `<script>` не работает | Почтовые клиенты вырезают JS | Использовать статический HTML |

---

## Готовые плейсхолдеры-примеры

Чтобы быстро скопировать:

**Письмо пользователю:**
```html
<!DOCTYPE html>
<html lang="ru">
<head><meta charset="utf-8"><title>Смена пароля</title></head>
<body>
  <h1>{{Greeting}}</h1>
  <p>Логин: {{UserName}}</p>
  <p>Пароль: <b>{{NewPassword}}</b></p>
  <p>Дата: {{Date}}</p>
  <hr>
  <p>{{CompanyName}} · {{CompanySite}} · {{CompanyEmail}}</p>
  <p style="font-size:11px;color:#888;">{{VendorLine}} — {{VendorUrl}}</p>
</body>
</html>
```

**Отчёт админам:**
```html
<!DOCTYPE html>
<html lang="ru">
<head><meta charset="utf-8"><title>Отчёт</title></head>
<body>
  <h1>Отчёт о смене паролей</h1>
  <p>Успешно: <b>{{TotalSuccess}}</b>, ошибок: <b>{{TotalErrors}}</b></p>
  <p>Следующая смена: {{NextRun}}</p>
  <table border="1" cellpadding="6" cellspacing="0">
    <thead>
      <tr><th>Пользователь</th><th>Email</th><th>Результат</th></tr>
    </thead>
    <tbody>
      {{Rows}}
    </tbody>
  </table>
  <p>Архив: {{ArchivePath}}</p>
  <p>Лог: {{LogPath}}</p>
</body>
</html>
```

---

## Обратная совместимость

Служба **не изменяет** ваши HTML-файлы. Что положили — то и отправится. Если добавите новые плейсхолдеры, которых нет в коде, они останутся в письме как есть (`{{MyCustom}}`) — так проще заметить опечатку.

Если хотите новые плейсхолдеры — их нужно добавить в `EmailSender.cs` в словарь `CommonBrandValues()`.

---

← [Назад к README](../README.md) · [← Конфигурация](CONFIG.md) · [Далее: Безопасность →](SECURITY.md)
