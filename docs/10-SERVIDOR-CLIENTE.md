# 10. Servidor de experiencias para el cliente

## Objetivo

La misma aplicación V20 abre una segunda superficie HTTP, separada del control
técnico. En la tablet del cliente se abre:

```text
http://IP-LAN-DE-V20:8781/
```

La portada pregunta **«¿Qué experiencia quieres vivir hoy?»**, agrupa el catálogo
por `Arquitectura de luz`, `Experiencia visual` y `Navidad`, y permite iniciar un
show mediante su nombre comercial. No muestra universos, IPs, rutas, fuentes,
playlists, test ni estado técnico.

## Catálogo comercial actual

| ID estable | Nombre para el cliente | Categoría |
|---|---|---|
| `skeewiff` | Ritmo de Luz | Experiencia visual |
| `santa` | La Voz de Santa | Navidad |
| `sarajevo` | Noche Eléctrica | Arquitectura de luz |
| `winter-wizard` | Hechizo de Invierno | Arquitectura de luz |
| `miser-brothers` | Fuego & Hielo | Experiencia visual |
| `here-comes-santa-claus` | La Llegada de Santa | Navidad |
| `sleigh-ride-8bit` | Trineo Pixel | Navidad |

Los IDs, playlists y rutas de archivos siguen siendo internos. Cambiar un
`publicTitle`, `category` o `tagline` no modifica el vínculo con xSchedule.

## API pública mínima

| Método | Ruta | Función |
|---|---|---|
| `GET` | `/health` | Salud básica del servidor. |
| `GET` | `/api/experiences` | Catálogo público y experiencia activa. |
| `POST` | `/api/experience/play` | Inicia un ID presente en el catálogo. |

Ejemplo de selección:

```http
POST /api/experience/play
Content-Type: application/json

{"id":"winter-wizard"}
```

El servidor reconstruye la respuesta con una lista blanca. Aunque el origen de
datos contenga información interna, sólo salen `id`, `publicTitle`, `category`,
`tagline` y `activeExperienceId`. Un ID desconocido se rechaza antes de llegar a
xSchedule; los errores internos se convierten en un mensaje genérico.

## Separación de superficies

El puerto 8781 sólo sirve `client.html`, `client.css` y `client.js`. Rutas como
`/api/source`, `/api/test`, `/api/state`, `/api/show/play` y los recursos de la
consola interna responden `404`. El operador continúa usando `http://IP:8780/`.

No hay autenticación en esta revisión. El aislamiento reduce errores y exposición,
pero no sustituye una red privada: cualquier equipo con acceso a 8781 podría
iniciar una experiencia válida. Bloquea ambos puertos fuera de la LAN del show.
