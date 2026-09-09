# 10. Servidor de experiencias para el cliente

## Objetivo

La misma aplicación V20 abre una segunda superficie HTTP, separada del control
técnico. En la tablet del cliente se abre:

```text
http://IP-LAN-DE-V20:8781/
```

La portada pregunta **«¿Qué experiencia quieres vivir hoy?»**, agrupa el catálogo
por `Arquitectura de luz`, `Experiencia visual` y `Navidad`, y permite iniciar un
show mediante su nombre comercial. También ofrece `SILUETA` y `PARTÍCULAS` como
interacciones en vivo. No muestra universos, IPs, rutas, fuentes, playlists, test
ni estado técnico.

## Galaxy Tab S10+ y pantalla completa

La interfaz R6 tiene una composición específica para tablets 16:10 en horizontal:
cuatro tarjetas por fila, alturas compactas y márgenes que respetan las áreas
seguras de pantalla. El manifiesto `client.webmanifest` declara orientación
`landscape` y presentación `fullscreen`.

Uso en la red del montaje:

1. Abre `http://IP-LAN-DE-V20:8781/` en la Galaxy Tab S10+.
2. Mantén la tablet en horizontal.
3. Al primer toque sobre la página, ésta solicita pantalla completa. Los
   navegadores no permiten hacerlo antes de una acción del usuario.
4. Si el navegador rechaza la solicitud, pulsa **Pantalla completa**. Si se abre
   desde un acceso instalado y el navegador admite el manifiesto en esa red, se
   solicita directamente la presentación sin barras.

El servidor continúa siendo HTTP dentro de la LAN privada; esta presentación no
implica cifrado ni autenticación.

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
| `zapravka-zavod` | Fábrica de Luz | Experiencia visual |
| `chicken-banana` | Fiesta Banana | Experiencia familiar |

Los IDs, playlists y rutas de archivos siguen siendo internos. Cambiar un
`publicTitle`, `category` o `tagline` no modifica el vínculo con xSchedule.

## API pública mínima

| Método | Ruta | Función |
|---|---|---|
| `GET` | `/health` | Salud básica del servidor. |
| `GET` | `/api/experiences` | Catálogo, experiencia activa y tracking filtrado. |
| `POST` | `/api/experience/play` | Inicia un ID presente en el catálogo. |
| `POST` | `/api/tracking/mode` | Solicita `silhouette` o `particles`. |

Ejemplo de selección:

```http
POST /api/experience/play
Content-Type: application/json

{"id":"winter-wizard"}
```

El servidor reconstruye la respuesta con una lista blanca. Aunque el origen de
datos contenga información interna, de cada show sólo salen `id`, `publicTitle`,
`category`, `tagline` y `enabled`, además de `activeExperienceId`. Una experiencia
con `enabled:false` permanece visible como `Próximamente`, pero el servidor rechaza
su ejecución antes de llegar a xSchedule. Los errores internos se convierten en un
mensaje genérico.

El bloque público `tracking` sólo contiene `configured`, `connected`, `status`,
`desiredMode`, `activeMode` y el catálogo permitido de modos; omite revisiones,
errores y datos de red. La página refresca `/api/experiences` cada 1,5 segundos.
Un clic en un modo indica **Preparando** hasta que el tracker confirma el mismo
modo y la misma revisión mediante el servidor interno; recién entonces aparece
**En vivo**.

## Fin, Stop y Pause

- Cuando un show lanzado por V20 termina, el monitor de xSchedule regresa la fuente
  a Resolume automáticamente.
- El comando Stop del operador también detiene xSchedule y regresa a Resolume.
- Pause conserva xLights como fuente y no dispara el retorno automático.
- El portal del cliente no expone Pause ni Stop; únicamente inicia experiencias.

Al 9 de septiembre de 2026, 08–10 están renderizados con audio utilizable. La 11
no tiene secuencia; la 12 tiene un audio aproximadamente 1,54 s más corto que el
render; y la 13 necesita la edición Radio Edit de 235,573 s. Por esa razón 11–13
permanecen como `Próximamente` para el cliente. El operador interno puede probar
12 y 13 con esas advertencias; 11 continúa bloqueado porque no tiene FSEQ.
Las experiencias 18 y 20 fueron promovidas manualmente al catálogo comercial:
**Fábrica de Luz** y **Fiesta Banana** están disponibles para reproducción.

## Separación de superficies

El puerto 8781 sólo sirve los recursos del cliente y su API mínima. Rutas como
`/api/source`, `/api/test`, `/api/state`, `/api/show/play`,
`/api/tracking/control`, `/api/tracking/status` y los recursos de la consola
interna responden `404`. El operador continúa usando `http://IP:8780/`.

No hay autenticación en esta revisión. El aislamiento reduce errores y exposición,
pero no sustituye una red privada: cualquier equipo con acceso a 8781 podría
iniciar una experiencia válida. Bloquea ambos puertos fuera de la LAN del show.
