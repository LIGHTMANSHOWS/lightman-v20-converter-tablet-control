# 2. Cómo interpretar el mapeo

El archivo autoritativo es `Data/SHOW-V20-MINIMAL-V4-PORTABLE.json`.

## Identidad y rutas

Cada objeto usa:

- `element_id`: identidad visual estable, por ejemplo `FLAG-3` o `WAVE-RIGHT`.
- `route_id`: buffer lógico que recibe V20, por ejemplo `flag3` o `waves-right`.
- `pixel_count`: cantidad de píxeles visuales del elemento.
- `visual`: geometría, posición, orientación y mapeo de píxeles.
- `bridge_input`: dirección Art-Net dentro del bloque de entrada.
- `physical_output`: destino posterior de V20, solo como referencia.

No usar la posición del elemento dentro del arreglo JSON como identidad.

## Regla principal de píxel

Para el píxel visual `i` de un elemento:

```text
routePixel = element.visual.pixel_mapping.route_pixel_indices[i]
routeBuffer[routePixel] = colorVisual[i]
```

`route_pixel_indices` es una LUT completa, expandida y base cero. Ya incorpora:

1. Giro o espejo del contenido visual.
2. Snake/cableado físico.
3. Offset del elemento dentro de la ruta.

No se debe sumar nuevamente `route_offset` ni aplicar otra vez `direction`. Hacerlo
duplica el desplazamiento o el espejo y reproduce exactamente el tipo de error que
se observó con el mapeo anterior.

## Empaquetado de una ruta en universos

Cada ruta contiene un buffer RGB de `route.pixels × 3` bytes. Su inicio está en
`configuration.bridge_routes[].bridge_input`.

Para avanzar por Art-Net:

```text
capacidad = 510 canales por universo
copiar hasta el canal 510
pasar al universo siguiente y continuar en canal 1
```

No calcular con 512 canales útiles: los dos últimos canales están reservados.

## Operaciones visuales y geométricas

- `geometry_transform`: cambia dónde aparece físicamente la geometría.
- `visual_pixel_transform`: cambia cómo se proyecta el contenido sobre ella.
- `physical_wiring_transform`: representa el cableado/snake.
- `route_pixel_indices`: resultado final autoritativo de esas operaciones.

Las operaciones descriptivas ayudan al editor; para emitir señal debe preferirse
siempre la LUT final.

## Excepciones vigentes

- `A09` tiene 99 píxeles.
- `B10` tiene 99 píxeles.
- Las rutas `tree2` y `tree4` conservan 600 direcciones; la dirección 600 queda
  negra para no correr los objetos posteriores.
- La casa tiene 400 direcciones físicas, pero solo LED 48–352 son visibles.
- LED 1–47 y 353–400 de la casa deben enviarse negros.
- No existe línea LED en el piso de la casa.

