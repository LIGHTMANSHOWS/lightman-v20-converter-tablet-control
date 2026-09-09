# 9. Contrato propuesto para modos de Motion Tracking

> Propuesta para coordinación. La versión actual de V20 todavía no implementa
> estos endpoints.

## Objetivo

La tablet debe mostrar los modos que declara V20, no una lista escrita dentro del
HTML. V20 valida la selección, se convierte en la única autoridad de control y el
programa de Tracking confirma cuándo terminó de aplicar el modo.

## Catálogo

Archivo propuesto: `TrackingControl/modes.json`.

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

Los IDs deben ser únicos, estables, en minúsculas y sin convertirse en comandos,
rutas o direcciones arbitrarias.

## Selección desde la tablet

```http
POST /api/tracking/mode
Content-Type: application/json

{"id":"particles"}
```

Respuesta propuesta:

```json
{"ok":true,"id":"particles","revision":17}
```

La operación debe apagar el test, pausar xSchedule si corresponde, seleccionar la
fuente `tracking` y publicar una nueva revisión. Repetir el mismo ID debe ser
idempotente.

## Consulta del proceso remoto

El backend nativo de Tracking puede consultar V20 cada 500 ms:

```http
GET /api/tracking/control
```

```json
{
  "apiVersion": 1,
  "revision": 17,
  "selected": true,
  "desiredMode": "particles"
}
```

Una página web remota no podrá hacerlo directamente mientras V20 mantenga CORS
bloqueado. Debe consultar un proceso nativo/backend o acordarse otro transporte
autenticado.

## Confirmación del tracker

```http
POST /api/tracking/status
Content-Type: application/json

{
  "clientId": "tracker-main",
  "revision": 17,
  "activeMode": "particles",
  "status": "running",
  "error": ""
}
```

Estados sugeridos: `starting`, `running`, `degraded` y `error`. V20 calcula
`offline` cuando vence el heartbeat.

## Estado para la tablet

`GET /api/state` debería incorporar:

```json
{
  "tracking": {
    "configured": true,
    "connected": true,
    "status": "running",
    "desiredMode": "particles",
    "activeMode": "particles",
    "revision": 17,
    "lastSeenUtc": "2026-09-08T12:00:00Z",
    "error": "",
    "modes": []
  }
}
```

La tablet dibuja sus botones desde `tracking.modes`. `desiredMode` representa la
orden pendiente; `activeMode` solo cambia cuando el programa remoto confirma la
misma revisión.

## Separación de transporte

- Los endpoints anteriores controlan el modo semántico.
- Art-Net transporta exclusivamente el cuadro RGB resultante.
- El tracker nunca recibe ni modifica IPs físicas desde la tablet.
- V20 conserva blackout, timeout y arbitraje de fuente como capa de seguridad.

