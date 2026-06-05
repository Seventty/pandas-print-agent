# PANDAS Print Agent

Agente local para imprimir tickets POS desde un backend Paleden en produccion.

## Flujo

1. El usuario presiona `Imprimir` en Pedidos o Facturacion.
2. El backend crea un trabajo en la cola `PrintJob`.
3. La app desktop del agente consulta el backend cada pocos segundos.
4. El agente imprime el ticket al POS local por TCP, normalmente `10.0.0.28:9100`.
5. El agente marca el trabajo como impreso o fallido.

## GUI desktop

El flujo recomendado es `Pandas.PrintAgent.App`, una app Avalonia con icono en bandeja/menu bar. La ventana permite editar:

- Backend URL y API prefix.
- Token del agente.
- Host, puerto, timeout e intervalo de polling.
- Uso opcional de `targetHost/targetPort` del trabajo.
- Logs y payload dumps.

Botones disponibles:

- `Guardar`: guarda settings no sensibles en `appsettings.json` y token en almacenamiento seguro.
- `Reload`: reinicia el worker interno sin relanzar la app.
- `Status`: llama `GET /api/print-agent/status` sin consumir trabajos.
- `Probar impresora`: envia una prueba ESC/POS directa al POS.
- `Abrir logs`: abre el archivo o carpeta de logs.
- `Salir`: detiene el worker y cierra la app.

Cerrar la ventana solo la oculta; el agente sigue corriendo en segundo plano desde la bandeja.

## Token seguro

`AgentToken` ya no se guarda en `appsettings.json`. La GUI lo guarda en:

- Windows: Credential Manager.
- macOS: Keychain.
- Linux: Secret Service via `secret-tool`.

En Linux instala `libsecret-tools` si la app indica que `secret-tool` no esta disponible. No hay fallback automatico a texto plano.

Para automatizacion/CLI se puede usar `PALEDEN_PRINT_AGENT_TOKEN`. El agente tambien puede leer un `AgentToken` legado si existe en un `appsettings.json` anterior, pero al guardar desde la GUI no se vuelve a escribir.

## Configuracion

`appsettings.json` contiene solo valores no sensibles:

```json
{
  "BackendBaseUrl": "https://tu-backend-paleden.example.com",
  "ApiPrefix": "api",
  "PollIntervalMs": 2000,
  "PrinterHost": "10.0.0.28",
  "PrinterPort": 9100,
  "PrinterTimeoutMs": 5000,
  "UseJobPrinterTarget": false,
  "LogFilePath": "logs/print-agent.log",
  "SavePayloads": false,
  "PayloadDumpDirectory": "logs/payloads"
}
```

En el backend de produccion configura:

```env
PRINT_DELIVERY_MODE=queue
PRINT_AGENT_TOKEN=mismo-token-que-el-agente
```

## Backend status

El backend expone:

```http
GET /api/print-agent/status
X-Print-Agent-Token: <token>
```

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

Si esa prueba dice `Prueba enviada` pero no sale papel, la conexion TCP al puerto 9100 acepta bytes, pero la impresora no los esta procesando fisicamente. Revisa IP, puerto, papel, tapa, estado interno del POS o configuracion de red/RAW printing.

Para guardar el payload binario de cada trabajo y poder compararlo, activa temporalmente:

```json
"SavePayloads": true
```

Los archivos quedan en `logs/payloads/`.

## Autostart

- Windows: crear un shortcut al `.exe` publicado dentro de la carpeta Startup o usar un instalador.
- macOS: crear un LaunchAgent que ejecute la app publicada.
- Linux: crear una entrada `.desktop` de autostart; el soporte de tray depende del desktop environment.
