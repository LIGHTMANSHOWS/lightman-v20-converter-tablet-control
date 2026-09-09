# 6. Servidor interno de operador y API actual

## Topología

V20 levanta un servidor HTTP en todas las interfaces (`0.0.0.0`) y puerto `8780`.
La tablet, conectada al mismo router, abre:

```text
http://IP-LAN-DE-V20:8780/
```

La IP de la propia tablet no se configura en V20. El programa muestra la dirección
preferida de la laptop; actualmente prioriza una IPv4 dentro de `192.168.1.0/24`.

El navegador de la tablet sirve `tablet.html`, `tablet.css` y `tablet.js` desde la
misma aplicación. Ésta es la superficie interna del operador. La versión pública
para el cliente usa el segundo servidor descrito en
[`10-SERVIDOR-CLIENTE.md`](10-SERVIDOR-CLIENTE.md).

## Endpoints implementados

| Método | Ruta | Cuerpo JSON | Función |
|---|---|---|---|
| `GET` | `/health` | — | Salud básica del servidor. |
| `GET` | `/api/state` | — | Estado de V20, señal, fuente, xSchedule y shows. |
| `GET` | `/api/tracking/control` | — | Orden vigente que consulta el backend de Tracking. |
| `POST` | `/api/source` | `{"source":"resolume"}` | Selecciona `resolume`, `xlights` o `tracking`. |
| `POST` | `/api/test` | `{"enabled":true}` | Inicia o detiene el test general. |
| `POST` | `/api/tracking/mode` | `{"id":"particles"}` | Solicita un modo permitido y devuelve su revisión. |
| `POST` | `/api/tracking/status` | Ver contrato | Confirma modo, revisión y heartbeat del tracker. |
| `POST` | `/api/show/play` | `{"id":"skeewiff"}` | Reproduce por ID un show permitido. |
| `POST` | `/api/show/pause` | `{}` | Pausa o continúa xSchedule. |
| `POST` | `/api/show/stop` | `{}` | Detiene xSchedule y vuelve a Resolume. |

Todas las solicitudes `POST` necesitan `Content-Type: application/json`. Si el
navegador envía `Origin`, debe coincidir con el `Host` de V20. CORS está bloqueado:
una página alojada en otra PC no puede controlar esta API directamente.

La versión actual no exige token o contraseña. La comprobación de `Origin` es una
protección del navegador, no autenticación: un cliente LAN que omita esa cabecera
podría enviar comandos. Mantén 8780 limitado a la red privada del show.

## Estado publicado

`GET /api/state` devuelve, entre otros:

```json
{
  "source": "tracking",
  "sourceLabel": "Motion Tracking",
  "generalTest": false,
  "signal": true,
  "packetRate": 44.0,
  "rx": 12345,
  "tx": 67890,
  "sender": "192.168.1.50",
  "error": "",
  "tabletError": "",
  "scheduler": {
    "configured": true,
    "connected": true,
    "status": "Idle",
    "playlist": "",
    "step": "",
    "position": "",
    "length": "",
    "error": ""
  },
  "tracking": {
    "configured": true,
    "connected": true,
    "selected": true,
    "status": "running",
    "desiredMode": "particles",
    "activeMode": "particles",
    "revision": 17,
    "lastSeenUtc": "2026-09-09T12:00:00Z",
    "error": "",
    "modes": [
      {"id":"silhouette", "title":"SILUETA", "description":"Contorno del cuerpo", "enabled":true},
      {"id":"particles", "title":"PARTÍCULAS", "description":"Partículas reactivas", "enabled":true}
    ]
  },
  "shows": [],
  "endpoints": {
    "resolume": "127.0.0.2:6454",
    "xlights": "127.0.0.3:6454",
    "tracking": "IP LAN de V20:6454"
  }
}
```

La tablet consulta este estado periódicamente. No debe inventar estados locales:
la respuesta de V20 es la fuente de verdad.

## Conmutación y xSchedule

- Al lanzar un show, V20 ordena la playlist a xSchedule y selecciona `xlights`.
- Al salir de xLights hacia Resolume o Tracking, V20 pausa xSchedule.
- El test general también pausa el show.
- Al terminar naturalmente el show, V20 confirma el estado final de xSchedule y
  vuelve automáticamente a `resolume`.
- `POST /api/show/stop` detiene el show y vuelve inmediatamente a `resolume`.
- `POST /api/show/pause` sólo alterna pausa/reproducción: una pausa conserva
  `xlights` como fuente y no provoca el retorno.
- xSchedule solo es accesible para V20 por `127.0.0.1`; la tablet no lo controla
  directamente.

El monitor de final se arma al lanzar un show desde V20 y también al volver a
xLights si xSchedule ya está en `Playing` o `Paused`. Evita confundir el breve
`Idle` inicial con un final y requiere dos lecturas `Idle` consecutivas.
Una caída del API no interrumpe el show mientras xLights siga enviando Art-Net;
V20 sólo retorna por desconexión cuando también confirma la ausencia de señal.

## Modos de Tracking implementados

El catálogo local de R6 publica dos IDs permitidos: `silhouette` y `particles`.
La selección puede hacerse desde el servidor interno o desde la superficie del
cliente:

```http
POST /api/tracking/mode
Content-Type: application/json
```

```json
{"id":"particles"}
```

V20 responde al aceptar la orden:

```json
{"ok":true,"id":"particles","revision":17}
```

Esa respuesta confirma la orden, no que el efecto ya esté activo. El backend de
Tracking consulta `GET /api/tracking/control`, aplica el modo y responde a
`POST /api/tracking/status` con el mismo `activeMode` y la misma `revision`. V20
rechaza revisiones antiguas o modos distintos. El heartbeat vence a los dos
segundos; `connected` pasa a `false` y `status` se publica como `offline`.

Reglas vigentes:

1. Los IDs provienen de un catálogo local permitido; la tablet no envía IPs,
   ejecutables, rutas ni comandos libres.
2. V20 actúa como broker: valida el ID, ordena el modo al proceso de Tracking y
   selecciona la fuente `tracking`; si venía de xLights, pausa el show.
3. `desiredMode` es la orden pendiente. `activeMode` sólo representa el modo
   confirmado por el proceso remoto.
4. El detalle completo de polling y ACK está en
   [`09-CONTRATO-MODOS-TRACKING.md`](09-CONTRATO-MODOS-TRACKING.md).
