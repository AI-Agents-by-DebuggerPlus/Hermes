# Hermes.WpGallery.Tool

Отдельное WPF-приложение: захват экрана и отправка в WordPress (**hermes-image-receiver**).

Основной агент **Hermes.Wpf** использует ту же библиотеку `Hermes.WpGallery` и публикует скриншоты автоматически после desktop capture.

## Настройки

`%AppData%\Hermes.WpGallery.Tool\settings.json`

## Сборка

```powershell
dotnet build -c Release
dotnet run --project Hermes.WpGallery.Tool.csproj
```
