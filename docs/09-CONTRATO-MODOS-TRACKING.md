# 9. Contrato implementado para modos de Motion Tracking

## Objetivo

R6 permite que la tablet elija un modo semántico mientras V20 conserva la
autoridad sobre la fuente Art-Net. El programa remoto de Tracking consulta la
orden, aplica el modo y confirma la misma revisión. Art-Net continúa transportando
únicamente el cuadro RGB resultante.

## Catálogo vigente

V20 carga `TrackingControl/modes.json` al iniciar:

```json
{
  "schemaVersion": 1,
  "modes": [
    {
      "id": "silhouette",
      "title": "SILUETA",
      "description": "Contorno del cuerpo",
      "enabled": true
    },
    {
      "id": "particles",
      "title": "PARTÍCULAS",
      "description": "Partículas reactivas",
      "enabled": true
    }
  ]
}
```

Los IDs son una lista blanca local: únicos, estables, en minúsculas y sin
convertirse en comandos, rutas o direcciones arbitrarias.

## 1. Selección del modo

El operador puede usar el puerto `8780` y el cliente el puerto `8781`:

```http
POST /api/tracking/mode
Content-Type: application/json

{"id":"particles"}
```

Respuesta al aceptar la orden:

```json
{"ok":true,"id":"particles","revision":17}
```

V20 apaga el test general, pausa xSchedule si estaba saliendo por xLights,
selecciona `tracking`, fija `desiredMode` y publica la revisión. Repetir el mismo
ID es idempotente y conserva esa revisión. El servidor cliente vuelve a validar el
ID contra el catálogo público y nunca expone el error operativo interno.

Esta respuesta no es el ACK del efecto: sólo confirma que V20 aceptó la solicitud.

## 2. Polling del proceso remoto

El backend nativo de Tracking consulta el servidor interno de V20; 500 ms es el
intervalo recomendado:

```http
GET http://IP-LAN-DE-V20:8780/api/tracking/control
```

```json
{
  "apiVersion": 1,
  "revision": 17,
  "selected": true,
  "desiredMode": "particles"
}
```

`selected` indica si Tracking es la fuente vigente. El backend debe conservar la
última revisión aplicada y sólo cambiar de modo cuando reciba una revisión nueva.
Estos endpoints se sirven por HTTP en la LAN. CORS permanece bloqueado, de modo que
el consumidor previsto es el backend nativo del tracker, no una página alojada en
otro origen.

## 3. ACK y heartbeat del tracker

Después de aplicar la orden, el tracker envía el mismo modo y revisión:

```http
POST http://IP-LAN-DE-V20:8780/api/tracking/status
Content-Type: application/json

{
  "clientId": "tracker-main",
  "revision": 17,
  "activeMode": "particles",
  "status": "running",
  "error": ""
}
```

Estados admitidos: `starting`, `running`, `degraded` y `error`. La confirmación es
aceptada únicamente cuando `revision` coincide con la vigente y `activeMode`
coincide con `desiredMode`. Una revisión desfasada responde `409`; datos inválidos
responden `400`.

```json
{"ok":true,"revision":17,"activeMode":"particles","status":"running"}
```

El tracker debe repetir el estado como heartbeat. V20 considera la conexión vencida
tras dos segundos sin una confirmación válida y publica `status: "offline"`.

## 4. Estado para las interfaces

El servidor interno incorpora el detalle en `GET /api/state`, incluidos
`selected`, `revision`, `lastSeenUtc` y `error`:

```json
{
  "tracking": {
    "configured": true,
    "connected": true,
    "selected": true,
    "status": "running",
    "desiredMode": "particles",
    "activeMode": "particles",
    "revision": 17,
    "modes": [
      {"id":"silhouette","title":"SILUETA","description":"Contorno del cuerpo","enabled":true},
      {"id":"particles","title":"PARTÍCULAS","description":"Partículas reactivas","enabled":true}
    ]
  }
}
```

El servidor cliente publica dentro de `GET /api/experiences` una versión filtrada
sin revisión, errores, fecha ni datos de red:

```json
{
  "tracking": {
    "configured": true,
    "connected": true,
    "status": "running",
    "desiredMode": "particles",
    "activeMode": "particles",
    "modes": [
      {"id":"silhouette","title":"SILUETA","description":"Contorno del cuerpo","enabled":true},
      {"id":"particles","title":"PARTÍCULAS","description":"Partículas reactivas","enabled":true}
    ]
  }
}
```

La superficie pública consulta `/api/experiences` cada 1,5 segundos. Muestra
**Preparando** mientras sólo coincide `desiredMode`; muestra **En vivo** cuando el
ACK válido produce `activeMode` y el heartbeat sigue vigente. Al seleccionar un
show o Resolume, V20 deja de presentar un modo de tracking como activo.

## Separación de transporte

- Los endpoints anteriores controlan el modo semántico.
- Art-Net transporta exclusivamente el cuadro RGB resultante hacia V20.
- El tracker envía a la IP Ethernet de V20 y no directamente a las IP físicas.
- El tracker nunca recibe ni modifica IPs físicas desde la tablet.
- V20 conserva blackout, timeout y arbitraje de fuente como capa de seguridad.
