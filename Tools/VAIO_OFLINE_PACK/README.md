# VAIO_OFLINE_PACK builder

Скрипт для **рабочего ПК с интернетом**. Готовый пакет для копирования на флешку:

`Tools/VAIO_OFLINE_PACK/READY_FOR_USB/VAIO_OFLINE_PACK/`

## Сборка / обновление пакета

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-VaioOfflinePack.ps1 -DestinationRoot .\READY_FOR_USB
```

## Содержимое

| Папка | Что |
|--------|-----|
| `1_NET_Framework_48` | `.NET Framework 4.8` offline + Dev Pack |
| `2_Drivers_WiFi` | Intel / Atheros / Broadcom (часть — вручную) |
| `3_Drivers_LAN` | Realtek (часто вручную; у VAIO LAN часто Atheros) |
| `4_Fixes_Windows7` | KB4490628 + KB4474419 |

На VAIO: `install_all.bat` от администратора.
