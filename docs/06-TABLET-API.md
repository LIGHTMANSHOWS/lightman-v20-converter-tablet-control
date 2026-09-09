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
| `POST` | `/api/source` | `{"source":"resolume"}` | Selecciona `resolume`, `xlights` o `tracking`. |
| `POST` | `/api/test` | `{"enabled":true}` | Inicia o detiene el test general. |
| `POST` | `/api/show/play` | `{"id":"skeewiff"}` | Reproduce por ID un show permitido. |
| `POST` | `/api/show/pause` | `{}` | Pausa o continúa xSchedule. |
| `POST` | `/api/show/stop` | `{}` | Detiene xSchedule. |

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
- xSchedule solo es accesible para V20 por `127.0.0.1`; la tablet no lo controla
  directamente.

## Extensión propuesta para modos de Tracking

Estos endpoints **todavía no están implementados**. Son el contrato recomendado
para añadir los botones solicitados sin exponer comandos arbitrarios en la tablet:

```http
GET /api/state
```

```json
{
  "tracking": {
    "connected": true,
    "activeMode": "silhouette",
    "modes": [
      {"id":"silhouette", "title":"Silueta", "enabled":true},
      {"id":"particles", "title":"Partículas", "enabled":true}
    ]
  }
}
```

```http
POST /api/tracking/mode
Content-Type: application/json

{"id":"particles"}
```

Reglas recomendadas:

1. Los IDs provienen de un catálogo local permitido; la tablet no envía IPs,
   ejecutables, rutas ni comandos libres.
2. V20 actúa como broker: valida el ID, ordena el modo al proceso de Tracking y
   recién entonces selecciona la fuente `tracking`.
3. El estado confirmado del proceso remoto vuelve a `/api/state`; un clic no se
   considera éxito hasta recibir confirmación.
4. Si Tracking deja de enviar Art-Net durante 2 segundos, V20 conserva la política
   segura vigente y pone la salida correspondiente en negro.
