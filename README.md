<p align="center">
  <img src="docs/logo.png" width="96" alt="Rift">
</p>

<h1 align="center">Rift</h1>

<p align="center">
  Быстрый файловый менеджер для Windows с вкладками и панелями бок о бок.
</p>

![Rift, тёмная тема](docs/screenshot-dark.png)

<details>
<summary>Светлая тема</summary>

![Rift, светлая тема](docs/screenshot-light.png)

</details>

> Ранняя стадия. Сейчас есть навигация, вкладки, две панели, тёмная и светлая темы.
> В работе: перетаскивание файлов между панелями, копирование и перемещение, иконки и контекстное меню Windows.

## Возможности

- Две панели рядом, у каждой свои вкладки и история
- Навигация по «крошкам» пути, назад / вперёд / вверх
- Быстрый доступ и диски с заполненностью в сайдбаре
- Тёмная и светлая темы
- Свой титлбар, без системной рамки

## Стек

- .NET 10, C#
- [Avalonia 12](https://avaloniaui.net/) + Fluent theme
- CommunityToolkit.Mvvm

## Структура

```
src/Rift.App     UI: окна, вьюхи, темы, ViewModel'и
src/Rift.Core    логика без UI: чтение папок, пути, история навигации, аргументы запуска
src/Rift.Shell   интеграция с Windows Shell (иконки, контекстное меню, IFileOperation) — в планах
tests/           тесты Rift.Core
design/          интерактивный макет дизайна (React), открыть design/preview.html
scripts/         иконка и демо-файлы для скриншотов
docs/            логотип и скриншоты
```

## Запуск

```bash
dotnet run --project src/Rift.App
```

Открыть конкретные папки (каждая — отдельная вкладка):

```bash
dotnet run --project src/Rift.App -- --left C:\Projects --left D:\Downloads --right E:\Backup
```

Тесты:

```bash
dotnet test
```

## Горячие клавиши

| Клавиша | Действие |
| --- | --- |
| Tab | Переключиться на другую панель |
| Ctrl+T / Ctrl+W | Новая вкладка / закрыть вкладку |
| Enter, двойной клик | Открыть |
| Backspace, Alt+↑ | Вверх |
| Alt+← / Alt+→ | Назад / вперёд |
| F5 | Обновить |

## Скриншоты и иконка

Скриншоты снимаются на мок-файлах, без личных папок:

```bash
powershell -File scripts/demo-fixture.ps1
dotnet run --project src/Rift.App -- --left C:\RiftDemo\Downloads --left C:\RiftDemo\Projects\rift --right C:\RiftDemo\Backup --right C:\RiftDemo\Media
powershell -File scripts/demo-fixture.ps1 -Remove
```

Иконка приложения генерируется скриптом `python scripts/make-icon.py` (нужен Pillow).

## Лицензия

[MIT](LICENSE)
