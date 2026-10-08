# Cuando conectes un dispositivo nuevo

Guía para los periféricos que todavía no se probaron (BlackShark V2 Pro Xbox, Flydigi Vader 5 Pro)
o cualquier otro.

## 1. ¿Aparece solo?

Conectalo, prendelo y en BatteryTray tocá ⟳ (o esperá 2 minutos). Hay varios proveedores que lo
pueden agarrar sin hacer nada:

| Si el dispositivo… | Lo toma |
|---|---|
| Es Razer y tiene la interfaz de 90 bytes de Razer | **Razer (HID)**: si no está en la tabla, prueba los transaction IDs conocidos solo |
| Es un mando que Windows reconoce (XInput / Xbox) | **Mandos (Windows.Gaming.Input)** |
| Está emparejado por Bluetooth y Windows muestra su batería en Configuración | **Bluetooth (Windows)** |
| Es un Flydigi Vader 5 Pro / Apex 5 / Apex 6 con el receptor o por cable | **Flydigi (HID)** (protocolo tomado de SDL) |
| Declara un campo HID estándar de batería | **HID genérico** |

Para ver qué hace cada proveedor: menú del ícono → "Abrir configuración…", poné `"verboseLog": true`,
guardá y mirá el log (menú → "Abrir carpeta de logs").

### Lo que se espera de cada uno

- **BlackShark V2 Pro (Xbox) por Bluetooth**: debería aparecer por *Bluetooth (Windows)* si Windows
  muestra su batería en Configuración → Bluetooth.
- **BlackShark V2 Pro (Xbox) por su receptor USB-C**: no hay documentación pública. Si el receptor
  usa el protocolo de Razer, lo detecta *Razer (HID)*; si declara la batería estándar, *HID genérico*.
  Si no aparece, ver el paso 2.
- **Flydigi Vader 5 Pro**: lo toma *Flydigi (HID)*, en pasos de 20 %. Si el receptor está en modo
  Xbox 360 (XInput) no expone la interfaz de Flydigi: probá cambiar el modo del mando (ver el
  manual) o mirá si aparece en *Mandos*.

## 2. Si no aparece: diagnóstico

Bajá `BatteryTray.Diag.exe` del release y corrélo con el dispositivo conectado y prendido. Genera un
`diag-AAAAMMDD-HHMMSS.txt` con:

- todos los dispositivos HID (VID/PID, interfaces, usage pages, largos de reports),
- todos los USB (incluye receptores que no son HID),
- Bluetooth emparejado con lo que Windows expone de su batería,
- mandos de Windows.Gaming.Input.

Con ese archivo se puede ver qué interfaz tiene y qué proveedor habría que escribir. El reporte
incluye direcciones Bluetooth: revisalo antes de compartirlo.

## 3. Si el fabricante tiene una app que sí muestra la batería: captura USB

Es como se resolvió el teclado: hay que ver qué le pide la app oficial al dispositivo.

1. Instalar [Wireshark](https://www.wireshark.org/) marcando **USBPcap** en el instalador, y reiniciar.
2. Abrir Wireshark como administrador, elegir la interfaz `USBPcapN` donde está el receptor.
3. Filtro: `usb.idVendor == 0x1532` (Razer), `usb.idVendor == 0x37d7` (Flydigi), o
   `usb.addr contains "X.Y"` con la dirección del dispositivo.
4. Abrir la app oficial (Razer Synapse / Flydigi Space Station), esperar a que muestre la batería,
   y cerrar la captura. Repetir con otro nivel de batería (o cargando) para comparar.
5. Buscar paquetes `URB_CONTROL` / `SET_REPORT` / `GET_REPORT` o `URB_INTERRUPT out` seguidos de una
   respuesta: el byte que cambia entre capturas suele ser el nivel.

## 4. Agregar un proveedor

Cada familia es una clase que implementa `IBatteryProvider` en `src/BatteryTray.Core/Providers`
(ver `Flydigi/` o `Keychron/` como ejemplos chicos) y se registra en `ProviderFactory.cs` y
`Config/AppConfig.cs`. Reglas que conviene mantener:

- Solo comandos de **lectura**; nunca escribir configuración ni "adquirir" el dispositivo.
- Si no responde, devolver nada (o `Connected = false` si ya respondía antes): el monitor conserva
  el último nivel.
- Tests del decodificador con bytes reales capturados (`tests/BatteryTray.Core.Tests`).
