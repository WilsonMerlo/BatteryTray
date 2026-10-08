# BatteryTray

App de bandeja para Windows 11 que muestra en un solo lugar la batería de todos los
periféricos inalámbricos: mouse, teclado, auriculares, AirPods y mandos.

- C# / .NET 8 (`net8.0-windows10.0.19041.0`, para usar las APIs WinRT de Bluetooth y Gaming.Input)
- Sin drivers, sin permisos de administrador y sin interferir con Razer Synapse.

## Estado

| Fase | Contenido | Estado |
|------|-----------|--------|
| 0 | Herramienta de diagnóstico (consola) | ✅ |
| 1 | Proveedores: Razer, AirPods, mando Xbox, Bluetooth genérico | pendiente |
| 2 | Interfaz de bandeja, autoarranque, notificaciones | pendiente |
| 3 | Investigación: Flydigi, BlackShark V2 Pro Xbox, Keychron 2.4 GHz | pendiente |

## Estructura

```
BatteryTray.sln
Directory.Build.props        configuración común (TFM, nullable, etc.)
tools/BatteryTray.Diag/      fase 0: diagnóstico por consola
src/                         (fases 1-2) Core con los proveedores y App de bandeja
```

## Requisitos

- Windows 10 2004 (19041) o superior; probado en Windows 11.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.

## Fase 0 — Diagnóstico

Herramienta de **solo lectura**: enumera dispositivos y propiedades, no envía ningún
comando ni report a los periféricos.

```
dotnet build -c Release
tools\BatteryTray.Diag\bin\Release\net8.0-windows10.0.19041.0\BatteryTray.Diag.exe
```

Opciones:

| Opción | Descripción |
|--------|-------------|
| `--ble <segundos>` | Duración del escaneo BLE (default 15; `0` lo desactiva) |
| `--no-gatt` | No leer el servicio GATT de batería de los dispositivos BLE |
| `--out <archivo>` | Ruta del reporte (default `diag-AAAAMMDD-HHMMSS.txt` en la carpeta actual) |

Secciones del reporte:

1. **HID** (HidSharp): VID/PID, interfaz USB (`MI`), colección (`COL`), usage page/usage,
   largos máximos y IDs de los input/output/feature reports.
2. **USB**: todos los dispositivos USB presentes, aunque no sean HID (receptores XInput,
   audio USB, adaptadores), con su clase y driver.
3. **Bluetooth emparejado**: clásico y LE, estado de conexión, devnodes asociados,
   la propiedad de batería que publica Windows y el servicio GATT Battery (0x180F).
4. **Mandos** (Windows.Gaming.Input): VID/PID y `BatteryReport`.
5. **Escaneo BLE**: advertisements de Apple "proximity pairing" (tipo `0x07`) en crudo,
   para identificar los AirPods.

El reporte incluye direcciones Bluetooth de tus dispositivos; tenelo en cuenta si lo compartís.

## Dispositivos identificados

| Dispositivo | Conexión | VID:PID | Notas |
|-------------|----------|---------|-------|
| Razer Viper V3 Pro SE | receptor 2.4 GHz / cable | `1532:00DF` | MI_00 con feature report de 91 B (protocolo Razer) |
| Keychron K8 HE | cable | `3434:0E80` | MI_01: raw HID QMK (`FF60:0061`, 32 B) |
| Keychron Link (receptor 2.4 GHz) | USB | `3434:D030` | MI_03: raw HID (`FF60:0061`); MI_01: page `0x8C` con feature reports de 1000 B |
| AirPods 4 ANC | Bluetooth | BT `004C:201B` | — |
| Mando Xbox | — | — | hay un Xbox Wireless Adapter `045E:02FE` conectado |
| BlackShark V2 Pro Xbox | — | — | pendiente |
| Flydigi Vader 5 Pro | — | — | pendiente |
