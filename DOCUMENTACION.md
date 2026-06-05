# PANDAS Print Agent - Documentacion completa

## 1. Resumen no tecnico

PANDAS Print Agent es una aplicacion local que permite imprimir tickets POS desde Paleden ERP aunque el sistema principal este en la nube.

El problema que resuelve es simple: el backend cloud no siempre puede conectarse directamente a una impresora fisica dentro de una tienda, porque esa impresora esta en una red local privada. Para resolverlo, este agente se ejecuta en una computadora dentro de la tienda, consulta el backend por trabajos pendientes y envia los tickets a la impresora local.

Flujo general:

1. Un usuario presiona `Imprimir` en Paleden ERP.
2. El backend crea un trabajo de impresion en cola.
3. PANDAS Print Agent, instalado en la PC local, toma ese trabajo.
4. El agente manda el ticket a la impresora POS por TCP, normalmente al puerto `9100`.
5. El agente informa al backend si la impresion fue exitosa o fallida.

La aplicacion tiene una interfaz grafica de escritorio con icono en la bandeja del sistema. Desde ahi se configuran el backend, el token, la impresora, los logs y las pruebas de impresion.

## 2. Para que sirve

El proyecto sirve para:

- Imprimir tickets POS en una impresora local desde un backend remoto.
- Evitar que el backend cloud necesite acceso directo a la red privada de la tienda.
- Mantener una cola de trabajos de impresion confiable.
- Reintentar trabajos fallidos desde el backend.
- Diagnosticar problemas de token, backend, red o impresora.
- Guardar logs locales para soporte.
- Probar la impresora sin depender del backend.

## 3. Usuarios objetivo

Usuarios operativos:

- Encargados de tienda.
- Cajeros.
- Personal que necesita que los tickets salgan automaticamente.

Usuarios tecnicos:

- Desarrolladores de Paleden ERP.
- Soporte tecnico.
- Administradores que instalan el agente en PCs de tienda.

## 4. Estructura del proyecto

El proyecto vive como carpeta hermana del backend y frontend:

```text
paleden/
  paleden-ERP/
  paleden-erp-backend/
  pandas-print-agent/
```

Dentro de `pandas-print-agent`:

```text
pandas-print-agent/
  Pandas.PrintAgent.sln
  Pandas.PrintAgent/
  Pandas.PrintAgent.App/
  Pandas.PrintAgent.Core/
  Pandas.PrintAgent.Tests/
  publish-app.ps1
  publish-app-win-x64.ps1
  publish-app-linux-x64.ps1
  publish-app-osx-x64.ps1
  publish-app-osx-arm64.ps1
```

Proyectos:

- `Pandas.PrintAgent.App`: app desktop Avalonia con bandeja del sistema.
- `Pandas.PrintAgent`: ejecutable CLI/diagnostico.
- `Pandas.PrintAgent.Core`: logica compartida entre GUI y CLI.
- `Pandas.PrintAgent.Tests`: pruebas unitarias del core.

## 5. Que se implemento

Se implemento una version completa del agente local de impresion:

- Refactor del agente anterior de un solo `Program.cs` a servicios reutilizables.
- Nueva solucion `.NET` independiente en `pandas-print-agent`.
- App desktop cross-platform con Avalonia UI.
- Icono en bandeja/menu bar.
- Ventana de configuracion y estado.
- Worker de impresion en segundo plano.
- Boton `Reload` para reiniciar el worker sin cerrar la app.
- Boton `Probar impresora` para enviar un ticket ESC/POS directo.
- Boton `Status` para validar backend sin tomar trabajos reales.
- Boton `Abrir logs`.
- Almacenamiento seguro del token.
- Endpoint backend `GET /api/print-agent/status`.
- Tests del backend para el endpoint de status.
- Tests .NET para settings, status, token seguro, seleccion de impresora y reload del worker.
- Scripts de publicacion self-contained para Windows, Linux y macOS.

## 6. Como funciona tecnicamente

### 6.1 Integracion con el backend

El backend Paleden ERP mantiene una cola de trabajos `PrintJob`.

El agente usa estos endpoints:

```http
POST /api/print-agent/jobs/next
POST /api/print-agent/jobs/:id/complete
POST /api/print-agent/jobs/:id/fail
GET  /api/print-agent/status
```

Todos requieren:

```http
X-Print-Agent-Token: <token>
```

El token debe coincidir con la variable del backend:

```env
PRINT_AGENT_TOKEN=<token>
```

### 6.2 Endpoint de status

El endpoint:

```http
GET /api/print-agent/status
```

responde:

```json
{
  "ok": true,
  "mode": "queue",
  "serverTime": "2026-06-02T00:00:00.000Z"
}
```

Este endpoint se agrego para que la GUI valide conectividad y token sin usar `jobs/next`. Esto es importante porque `jobs/next` reclama trabajos reales.

### 6.3 Worker de impresion

El worker hace un loop:

1. Crea un `HttpClient` con el header `X-Print-Agent-Token`.
2. Llama `POST /api/print-agent/jobs/next`.
3. Si no hay trabajo, espera `PollIntervalMs`.
4. Si hay trabajo:
   - Decodifica `payloadBase64`.
   - Decide la impresora destino.
   - Envia bytes por TCP.
   - Marca el trabajo como completo o fallido.
5. Reporta estado a la GUI.

Si la impresion falla, el agente llama:

```http
POST /api/print-agent/jobs/:id/fail
```

El backend decide si el trabajo puede reintentarse segun `PRINT_JOB_MAX_ATTEMPTS`, `PRINT_JOB_RETRY_DELAY_MS` y `PRINT_JOB_PROCESSING_TIMEOUT_MS`.

### 6.4 Seleccion de impresora

Configuracion principal:

```json
{
  "PrinterHost": "10.0.0.28",
  "PrinterPort": 9100,
  "UseJobPrinterTarget": false
}
```

Si `UseJobPrinterTarget` es `false`, siempre se usa la impresora configurada localmente.

Si `UseJobPrinterTarget` es `true`, el agente usa `targetHost` y `targetPort` del trabajo cuando el backend los envia.

### 6.5 Protocolo de impresion

La impresora recibe bytes ESC/POS por TCP raw.

Valores tipicos:

- Host: IP local de la impresora, por ejemplo `10.0.0.28`.
- Puerto: `9100`.
- Timeout: `5000` ms.

El agente no renderiza HTML ni PDF. El backend ya genera el payload ESC/POS en base64.

### 6.6 Configuracion

El archivo `appsettings.json` contiene datos no sensibles:

```json
{
  "BackendBaseUrl": "https://paleden-backend.hashteam.dev",
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

El token no se guarda en `appsettings.json` cuando se usa la GUI.

### 6.7 Token seguro

El token se guarda en almacenamiento seguro del sistema:

- Windows: Credential Manager.
- macOS: Keychain.
- Linux: Secret Service via `secret-tool`.

No hay fallback automatico a texto plano.

Para automatizacion o CLI se puede usar:

```bash
PALEDEN_PRINT_AGENT_TOKEN=<token>
```

El agente tambien puede leer un `AgentToken` legado si existe en un `appsettings.json` antiguo, pero al guardar desde la GUI no lo vuelve a escribir.

### 6.8 Variables de entorno soportadas

Las variables de entorno pueden sobrescribir `appsettings.json`:

| Variable | Uso |
| --- | --- |
| `PALEDEN_BACKEND_BASE_URL` | URL base del backend |
| `PALEDEN_API_PREFIX` | Prefijo de API, normalmente `api` |
| `PALEDEN_PRINT_AGENT_TOKEN` | Token del agente |
| `PALEDEN_PRINTER_HOST` | IP o host de la impresora |
| `PALEDEN_PRINTER_PORT` | Puerto TCP |
| `PALEDEN_POLL_INTERVAL_MS` | Intervalo de polling |
| `PALEDEN_PRINTER_TIMEOUT_MS` | Timeout de impresora |
| `PALEDEN_USE_JOB_PRINTER_TARGET` | Usa target del trabajo si esta disponible |
| `PALEDEN_PRINT_AGENT_LOG` | Ruta del log |
| `PALEDEN_SAVE_PAYLOADS` | Guarda payloads binarios |
| `PALEDEN_PAYLOAD_DUMP_DIRECTORY` | Carpeta de payload dumps |

## 7. Requisitos

### 7.1 Para ejecutar desde codigo fuente

- .NET SDK 8 o superior.
- PowerShell para scripts de publicacion.
- Acceso al backend Paleden ERP.
- Una impresora POS ESC/POS alcanzable desde la maquina local.

### 7.2 Para ejecutar publicado

Los builds son self-contained, por lo que el usuario final no necesita instalar .NET.

Requisitos operativos:

- PC encendida en la red local de la impresora.
- Acceso HTTP/HTTPS al backend.
- Token correcto.
- Impresora conectada por red TCP raw.

### 7.3 Requisito adicional en Linux

Para guardar el token de forma segura:

```bash
sudo apt-get install libsecret-tools
```

En distribuciones no Debian/Ubuntu, instala el paquete que provea `secret-tool`.

## 8. Como levantar en desarrollo

Desde la raiz del workspace:

```powershell
cd pandas-print-agent
dotnet restore Pandas.PrintAgent.sln
dotnet build Pandas.PrintAgent.sln
dotnet test Pandas.PrintAgent.sln
```

Ejecutar GUI:

```powershell
dotnet run --project Pandas.PrintAgent.App
```

Ejecutar CLI:

```powershell
dotnet run --project Pandas.PrintAgent
```

Ejecutar prueba directa de impresora:

```powershell
dotnet run --project Pandas.PrintAgent -- --test-print
```

## 9. Como preparar el backend

En `paleden-erp-backend`, configurar `.env`:

```env
PRINT_DELIVERY_MODE=queue
PRINT_AGENT_TOKEN=<token-compartido>
```

Levantar backend en desarrollo:

```powershell
cd paleden-erp-backend
npm install
npm run prisma:generate
npm run start:dev
```

API local:

```text
http://localhost:3000/api
```

En produccion, `BackendBaseUrl` debe ser la URL publica del backend, por ejemplo:

```text
https://paleden-backend.hashteam.dev
```

## 10. Como configurar la GUI

1. Ejecutar `Pandas.PrintAgent.App`.
2. Abrir la ventana desde el icono de bandeja si arranca oculta.
3. Configurar:
   - `Backend URL`
   - `API prefix`
   - `Token`
   - `Printer host`
   - `Printer port`
   - `Poll interval ms`
   - `Printer timeout ms`
4. Presionar `Guardar`.
5. Presionar `Status`.
6. Presionar `Probar impresora`.
7. Presionar `Reload`.

El boton `Guardar` guarda:

- Settings no sensibles en `appsettings.json`.
- Token en almacenamiento seguro del sistema.

El boton `Reload`:

- Detiene el worker actual.
- Recarga configuracion.
- Recrea el cliente HTTP.
- Reinicia polling sin abrir un segundo worker.

## 11. Como publicarlo por plataforma

Los scripts generan builds self-contained.

### 11.1 Windows x64

Desde PowerShell:

```powershell
cd pandas-print-agent
.\publish-app-win-x64.ps1
```

Salida:

```text
Pandas.PrintAgent.App/publish/win-x64/
```

Ejecutar:

```powershell
.\Pandas.PrintAgent.App\publish\win-x64\Pandas.PrintAgent.App.exe
```

Autostart recomendado:

1. Presionar `Win + R`.
2. Escribir `shell:startup`.
3. Crear un acceso directo a `Pandas.PrintAgent.App.exe`.

### 11.2 Linux x64

Desde PowerShell:

```powershell
cd pandas-print-agent
pwsh ./publish-app-linux-x64.ps1
```

Salida:

```text
Pandas.PrintAgent.App/publish/linux-x64/
```

Ejecutar:

```bash
cd Pandas.PrintAgent.App/publish/linux-x64
chmod +x Pandas.PrintAgent.App
./Pandas.PrintAgent.App
```

Instalar soporte de token seguro si falta:

```bash
sudo apt-get install libsecret-tools
```

Autostart recomendado:

Crear un archivo `.desktop` en:

```text
~/.config/autostart/pandas-print-agent.desktop
```

Ejemplo:

```ini
[Desktop Entry]
Type=Application
Name=PANDAS Print Agent
Exec=/ruta/a/pandas-print-agent/Pandas.PrintAgent.App/publish/linux-x64/Pandas.PrintAgent.App
Terminal=false
X-GNOME-Autostart-enabled=true
```

Nota: el soporte de tray puede variar segun el entorno de escritorio.

### 11.3 macOS x64

Desde PowerShell:

```powershell
cd pandas-print-agent
pwsh ./publish-app-osx-x64.ps1
```

Salida:

```text
Pandas.PrintAgent.App/publish/osx-x64/
```

Ejecutar:

```bash
cd Pandas.PrintAgent.App/publish/osx-x64
chmod +x Pandas.PrintAgent.App
./Pandas.PrintAgent.App
```

El token se guarda con Keychain usando `security`.

### 11.4 macOS Apple Silicon

Desde PowerShell:

```powershell
cd pandas-print-agent
pwsh ./publish-app-osx-arm64.ps1
```

Salida:

```text
Pandas.PrintAgent.App/publish/osx-arm64/
```

Ejecutar:

```bash
cd Pandas.PrintAgent.App/publish/osx-arm64
chmod +x Pandas.PrintAgent.App
./Pandas.PrintAgent.App
```

Autostart recomendado en macOS:

- Crear un LaunchAgent.
- O agregar la app a Login Items si se empaqueta como `.app` posteriormente.

## 12. Publicar CLI de diagnostico

El CLI se mantiene para diagnostico.

Desde Windows:

```powershell
cd pandas-print-agent\Pandas.PrintAgent
.\publish-win-x64.ps1
```

Salida:

```text
Pandas.PrintAgent/publish/win-x64/
```

Ejecutar prueba POS directa:

```powershell
.\Pandas.PrintAgent.exe --test-print
```

## 13. Logs y payload dumps

Log default:

```text
logs/print-agent.log
```

La ruta es relativa a la carpeta del ejecutable publicado.

Para guardar payloads binarios:

```json
{
  "SavePayloads": true,
  "PayloadDumpDirectory": "logs/payloads"
}
```

Esto permite comparar bytes recibidos del backend y diagnosticar problemas ESC/POS.

## 14. Diagnostico y soporte

### 14.1 Status dice token invalido

Revisar:

- Que `PRINT_AGENT_TOKEN` del backend sea igual al token guardado en la GUI.
- Que no haya espacios al copiar el token.
- Que la GUI haya guardado correctamente el token.

### 14.2 Status dice backend no alcanzable

Revisar:

- `BackendBaseUrl`.
- `ApiPrefix`.
- Conexion a internet.
- Certificado HTTPS si aplica.
- Que el backend este activo.

Probar manualmente:

```bash
curl -H "X-Print-Agent-Token: <token>" https://backend.example.com/api/print-agent/status
```

### 14.3 La prueba de impresora no imprime

Revisar:

- IP de la impresora.
- Puerto `9100`.
- Que la PC este en la misma red.
- Papel, tapa y estado fisico de la impresora.
- Que la impresora acepte RAW TCP.

### 14.4 El backend crea trabajos pero no salen tickets

Revisar:

- Que la GUI este corriendo.
- Que el worker este activo.
- Que `Status` responda conectado.
- Que `PollIntervalMs` no sea excesivo.
- Logs locales.
- Estado de trabajos `PrintJob` en backend.

### 14.5 Se imprimio duplicado o no se marco completo

Revisar:

- Logs del agente.
- Si hubo timeout despues de enviar bytes.
- `PRINT_JOB_PROCESSING_TIMEOUT_MS`.
- Conectividad entre agente y backend.

## 15. Comandos de verificacion

Verificar agente:

```powershell
cd pandas-print-agent
dotnet build Pandas.PrintAgent.sln
dotnet test Pandas.PrintAgent.sln
```

Verificar backend:

```powershell
cd paleden-erp-backend
npm test -- --runInBand
npm run build
```

## 16. Alcance actual y limites

Incluido:

- GUI desktop con tray/menu bar.
- CLI de diagnostico.
- Worker de polling.
- Almacenamiento seguro de token.
- Publicacion self-contained.
- Status endpoint sin consumir trabajos.
- Tests principales.

No incluido todavia:

- Instalador MSI/PKG/DEB.
- Empaquetado macOS como `.app`.
- Servicio Windows nativo.
- Systemd unit para Linux.
- Multiples perfiles de impresora desde la GUI.
- Dashboard remoto del agente.

## 17. Seguridad

Buenas practicas:

- Usar un `PRINT_AGENT_TOKEN` unico por entorno.
- No guardar el token en texto plano.
- Rotar el token si se comparte accidentalmente.
- Usar HTTPS para `BackendBaseUrl`.
- No activar `SavePayloads` en produccion salvo diagnostico temporal.

## 18. Resumen para operacion

Para instalar en una PC de tienda:

1. Publicar la app para la plataforma correcta.
2. Copiar la carpeta publicada a la PC.
3. Ejecutar `Pandas.PrintAgent.App`.
4. Configurar URL, token e impresora.
5. Presionar `Guardar`.
6. Presionar `Status`.
7. Presionar `Probar impresora`.
8. Dejar la app corriendo en bandeja.
9. Configurar autostart.

Cuando todo esta correcto, los tickets se imprimen automaticamente al presionar `Imprimir` desde Paleden ERP.
