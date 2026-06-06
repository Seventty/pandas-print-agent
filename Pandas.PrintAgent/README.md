# PANDAS Print Agent

Agente local para imprimir tickets POS desde un backend compatible en produccion.

## Flujo

1. El usuario presiona `Imprimir` en Pedidos o Facturacion.
2. El backend crea un trabajo en la cola `PrintJob`.
3. La app desktop del agente consulta el backend cada pocos segundos.
4. El agente imprime el ticket al POS local por el conector configurado.
5. El agente marca el trabajo como impreso o fallido.

## GUI desktop

El flujo recomendado es `Pandas.PrintAgent.App`, una app Avalonia con icono en bandeja/menu bar. La ventana permite editar:

- Backend URL y API prefix.
- Token del agente, opcional si el backend no exige autenticacion por token.
- Conector de impresora: `WiFi/Ethernet (TCP)`, `USB` o `Bluetooth`.
- Host/puerto para red TCP, o dropdown de impresoras instaladas para USB/Bluetooth.
- Timeout e intervalo de polling.
- Uso opcional de `targetHost/targetPort` del trabajo.
- Logs y payload dumps.

Botones disponibles:

- `Guardar`: guarda settings no sensibles en `appsettings.json`, guarda o limpia el token seguro y recarga el worker.
- `Reload`: reinicia el worker interno sin relanzar la app.
- `Status`: llama `GET /api/print-agent/status` sin consumir trabajos.
- `Probar WiFi/Ethernet`, `Probar USB` o `Probar Bluetooth`: envia una prueba ESC/POS directa al conector configurado.
- `Abrir logs`: abre el archivo o carpeta de logs.
- `Salir`: detiene el worker y cierra la app.

Cerrar la ventana solo la oculta; el agente sigue corriendo en segundo plano desde la bandeja.

## Token seguro

`AgentToken` es opcional. Si se deja vacio, PANDAS no envia el header `X-Print-Agent-Token`.

Cuando se configura un token, la GUI no lo guarda en `appsettings.json`; lo guarda en:

- Windows: Credential Manager.
- macOS: Keychain.
- Linux: Secret Service via `secret-tool`.

En Linux instala `libsecret-tools` si la app indica que `secret-tool` no esta disponible. No hay fallback automatico a texto plano.

Para automatizacion/CLI se puede usar `PANDAS_PRINT_AGENT_TOKEN`. El agente tambien puede leer un `AgentToken` legado si existe en un `appsettings.json` anterior, pero al guardar desde la GUI no se vuelve a escribir. Si guardas con el token vacio, PANDAS limpia el token guardado previamente.

## Configuracion

`appsettings.json` contiene solo valores no sensibles:

```json
{
  "BackendBaseUrl": "https://backend.example.com",
  "ApiPrefix": "api",
  "PollIntervalMs": 2000,
  "PrinterConnectorType": "NetworkTcp",
  "PrinterHost": "10.0.0.28",
  "PrinterPort": 9100,
  "PrinterQueueName": "",
  "PrinterTimeoutMs": 5000,
  "UseJobPrinterTarget": false,
  "LogFilePath": "logs/print-agent.log",
  "SavePayloads": false,
  "PayloadDumpDirectory": "logs/payloads"
}
```

`WiFi/Ethernet (TCP)` usa `PrinterHost` y `PrinterPort`. USB y Bluetooth muestran las impresoras instaladas detectadas por el sistema; al seleccionar una, PANDAS guarda su nombre en `PrinterQueueName`. Si conectas o emparejas una impresora con la app abierta, usa `Refrescar`.

Si el backend exige token, configura:

```env
PRINT_DELIVERY_MODE=queue
PRINT_AGENT_TOKEN=mismo-token-que-el-agente
```

Si el backend no exige token, deja el campo de token vacio en PANDAS.

## Backend status

El backend expone:

```http
GET /api/print-agent/status
X-Print-Agent-Token: <token>
```

El header `X-Print-Agent-Token` solo se envia cuando hay token configurado.

Respuesta esperada:

```json
{
  "ok": true,
  "mode": "queue",
  "serverTime": "2026-06-02T00:00:00.000Z"
}
```

Este endpoint no reclama ni modifica trabajos de impresion.

## Publicar

Desde `pandas-print-agent`:

```powershell
.\publish-app-win-x64.ps1
.\publish-app-linux-x64.ps1
.\publish-app-osx-x64.ps1
.\publish-app-osx-arm64.ps1
```

La GUI queda en `Pandas.PrintAgent.App/publish/<runtime>/`.

El CLI sigue disponible para diagnostico. Desde `Pandas.PrintAgent`:

```powershell
.\publish-win-x64.ps1
```

El ejecutable CLI queda en `publish/win-x64/`.

## Diagnostico CLI

Para probar el POS sin backend ni cola:

```powershell
.\Pandas.PrintAgent.exe --test-print
```

Si esa prueba dice `Prueba enviada` pero no sale papel, el conector acepto los bytes, pero la impresora no los esta procesando fisicamente. Revisa IP, puerto, nombre de impresora instalada, CUPS/Winspool, papel, tapa, estado interno del POS o configuracion RAW.

Para guardar el payload binario de cada trabajo y poder compararlo, activa temporalmente:

```json
"SavePayloads": true
```

Los archivos quedan en `logs/payloads/`.

## Autostart

- Windows: crear un shortcut al `.exe` publicado dentro de la carpeta Startup o usar un instalador.
- macOS: crear un LaunchAgent que ejecute la app publicada.
- Linux: crear una entrada `.desktop` de autostart; el soporte de tray depende del desktop environment.
