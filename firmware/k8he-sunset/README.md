# Firmware K8 HE "sunset" (v3)

Firmware propio para el **Keychron K8 HE ANSI** (`3434:0E80`), basado en el firmware oficial de
Keychron 1.1.1 (`Keychron/qmk_firmware`, rama `2025q3`, ver `BASE.txt`).

| Qué trae | Detalle |
|----------|---------|
| Keymap | El configurado en Keychron Launcher (export del 2026-10-08): el de Keychron, con F20/F21/F22 arriba a la derecha en la capa Windows. Verificado tecla por tecla contra el `.bin` compilado. |
| Sunset Wave | Efecto RGB propio; `Fn + Backspace` lo activa. |
| SignalRGB | Protocolo 1.0.6 (plugin "Keychron K8 HE QMK Keyboard"), solo por cable. |
| Batería | Comando raw HID `0xBA` (ver `battery_hid.h` en el parche). Lo usa BatteryTray. |
| 2.4 GHz | `WILRESS_RAW_ENABLE`: el teclado atiende comandos raw HID que llegan por el Keychron Link. |
| Perfil HE | Valores de fábrica = perfil "Default" exportado del Launcher (solo se usan si el perfil se resetea). |

## Compilar

Requiere [QMK MSYS](https://msys.qmk.fm/). En la consola de QMK MSYS:

```
git clone --depth 1 --branch 2025q3 https://github.com/Keychron/qmk_firmware.git
cd qmk_firmware
git submodule update --init --depth 1 lib/chibios lib/chibios-contrib lib/printf
git am /c/Users/Wilson/Downloads/SonarChatMix/BatteryTray/firmware/k8he-sunset/0001-k8he-sunset-signalrgb-bateria.patch
make keychron/k8_he/ansi:sunset
```

## Flashear

1. Teclado **por cable**, en modo cable.
2. Entrar al bootloader: desenchufar, mantener apretado el botón de reset (debajo de la barra
   espaciadora) o `Esc`, y enchufar. Aparece `STM32 BOOTLOADER` (`0483:DF11`).
3. QMK Toolbox → abrir el `.bin` → **Flash**.

Al arrancar, el keymap de VIA vuelve al de fábrica de este firmware (que es el tuyo). La
calibración, los perfiles HE, la curva y el RGB quedan como estaban.

Para volver atrás: flashear `K8HE_ANSI_sunset_signalrgb_v2.bin` de la misma forma.
