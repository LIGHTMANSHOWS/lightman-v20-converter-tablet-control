# 7. Integración de Motion Tracking

## Responsabilidad del software de Tracking

El programa remoto calcula colores en el orden visual del montaje, los adapta al
manifiesto V4 y envía ArtDMX RGB a la laptop V20. No debe hablar con las banderas,
con `.91`, `.92` ni `.93`.

Para el destino de red tampoco debe copiar `elements[].bridge_input.ip`,
`target_ip` ni `configuration.controller_ips`: esos campos reflejan el ingreso
local de otras fuentes o compatibilidad histórica. Del manifiesto se toman los
universos/canales de `configuration.bridge_routes[].bridge_input`; la IP de destino
siempre es la IP Ethernet que V20 muestra en la laptop.

```text
PC Tracking ── unicast UDP 6454 ──► IP Ethernet de V20
```

`127.0.0.2`, `127.0.0.3` y `127.0.0.4` son loopback de la máquina local. Desde la
PC de Tracking debe utilizarse la IP Ethernet real que V20 muestra en pantalla.

## Bloque de entrada

- Universos: `0–124`, base cero.
- Slots por universo: 512.
- Datos RGB usados: canales `1–510` = 170 píxeles.
- Canales `511–512`: cero/reservados.
- Protocolo: ArtDMX versión 14, UDP 6454, unicast.
- Orden de color: RGB.

Las 15 rutas reservan 20 320 posiciones RGB. Existen 20 223 píxeles visibles; los
slots reservados por A09/B10 y los LED ocultos de la casa deben conservarse negros,
no compactarse.

El software puede transmitir solo los universos que cambian, pero debe mantener
vigentes todos los fragmentos de una ruta. V20 considera una ruta inválida si un
fragmento no llega durante 2 segundos y la envía negra.

## Algoritmo autoritativo

Para cada elemento:

```text
routePixel = element.visual.pixel_mapping.route_pixel_indices[visualPixel]
routeBuffer[routePixel] = colorVisual[visualPixel]
```

Después se empaqueta `routeBuffer` empezando en
`configuration.bridge_routes[].bridge_input`. La LUT ya contiene offset, snake,
giro y espejo. No se suma `route_offset_pixels` otra vez.

El ejemplo ejecutable está en
[`integration/Examples/manifest_adapter.py`](../integration/Examples/manifest_adapter.py).

## Identidad y orientación

- Usa `element_id` como identidad estable; no uses la posición dentro del arreglo.
- Banderas por frente: 1–2 arriba izquierda, 3–4 arriba derecha, 5 abajo izquierda,
  6 abajo derecha.
- `FLAG-5` y `FLAG-6` llevan giro visual de 180°.
- `WAVE-RIGHT` incluye espejo X mediante su LUT; no lo apliques por segunda vez.
- El giro Y=180° de los grupos de techo es geométrico y no altera la señal.
- `A09` y `B10` tienen 99 píxeles; las reservas restantes deben quedar negras.
- En la casa solo son visibles los LED 48–352 de las 400 direcciones físicas.

## Detección de fuente

V20 escucha Art-Net en sus IPv4 activas. Una fuente remota se identifica como
Tracking por la IP de origen. Al seleccionar Tracking, V20 bloquea una sola IP de
emisor activa; no mezcla dos PCs ni hace failover por universo.

Durante integración:

1. Cierra otras copias de V20 y cualquier receptor que reserve UDP 6454.
2. Selecciona `TRACKING` en V20 o en la tablet.
3. Envía un patrón inequívoco y de bajo brillo a la IP Ethernet de V20.
4. Comprueba `sender`, `packetRate`, `rx`, `signal` y `error` en `/api/state`.
5. Valida el visualizador antes de conectar las salidas físicas.
6. Haz la primera prueba física con límite de brillo y posibilidad de blackout.

## Modos controlados desde la tablet

Art-Net transporta el resultado visual, no la orden semántica “usar partículas” o
“usar silueta”. Para esos botones hace falta un pequeño canal de control adicional
entre V20 y el programa remoto. El contrato sugerido está en
[`06-TABLET-API.md`](06-TABLET-API.md#extensión-propuesta-para-modos-de-tracking).

El flujo recomendado es:

```text
Tablet ─► V20 /api/tracking/mode ─► adaptador de control de Tracking
                                      │
                                      └─ confirmación de modo
PC Tracking ───────────── Art-Net ─────────────► V20
```

La implementación del transporte de control (HTTP local, WebSocket o UDP con ACK)
debe acordarse con el desarrollador de Tracking. No debe reutilizar Art-Net para
comandos que no sean datos DMX.

El contrato HTTP completo propuesto está en
[`09-CONTRATO-MODOS-TRACKING.md`](09-CONTRATO-MODOS-TRACKING.md).
