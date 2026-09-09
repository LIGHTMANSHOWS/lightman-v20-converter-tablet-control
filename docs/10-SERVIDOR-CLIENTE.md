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
| `this-is-halloween` | Noche de Halloween | Especial de Halloween |
| `light-em-up` | Enciende la Noche | Experiencia visual |
| `baby-shark-edm` | Océano Eléctrico | Experiencia familiar |
| `blinding-lights` | Ciudad de Neón | Experiencia visual |
| `believer` | Fuerza Imparable | Experiencia visual |
| `uptown-funk` | Ritmo en la Ciudad | Experiencia visual |

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
`tagline`, `enabled` y `activeExperienceId`. Una experiencia con `enabled:false`
permanece visible como `Próximamente`, pero el servidor rechaza su ejecución antes
de llegar a xSchedule. Los errores internos se convierten en un mensaje genérico.

Al 8 de septiembre de 2026, 08–10 están renderizados con audio utilizable. La 11
no tiene secuencia; la 12 tiene un audio aproximadamente 1,54 s más corto que el
render; y la 13 necesita la edición Radio Edit de 235,573 s. Por esa razón 11–13
permanecen deshabilitados aunque sus tarjetas estén en el catálogo.

## Separación de superficies

El puerto 8781 sólo sirve `client.html`, `client.css` y `client.js`. Rutas como
`/api/source`, `/api/test`, `/api/state`, `/api/show/play` y los recursos de la
consola interna responden `404`. El operador continúa usando `http://IP:8780/`.

No hay autenticación en esta revisión. El aislamiento reduce errores y exposición,
pero no sustituye una red privada: cualquier equipo con acceso a 8781 podría
iniciar una experiencia válida. Bloquea ambos puertos fuera de la LAN del show.
