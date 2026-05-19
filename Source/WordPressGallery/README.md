# WordPress Gallery (перенесено)

Исходный прототип `Hermes.Wpf` в этой папке **встроен в основной репозиторий Hermes**:

| Компонент | Путь |
|-----------|------|
| Общая библиотека REST/WebSocket | `Hermes.WpGallery/` |
| Отдельный инструмент (захват + отправка) | `Hermes.WpGallery.Tool/` |
| Интеграция в агент | `Hermes.Wpf/` → после desktop capture |

Плагин WordPress: **hermes-image-receiver**, шорткод `[hermes_gallery]`.

Сборка инструмента:

```powershell
dotnet build Hermes.WpGallery.Tool/Hermes.WpGallery.Tool.csproj -c Release
```

Старый каталог `Source/WordPressGallery/Hermes.Wpf` можно не использовать.
