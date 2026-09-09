# 4. Orientaciones visuales vigentes

Estas correcciones están dentro del JSON V4. No deben recrearse usando reglas
antiguas ni el orden de los objetos.

## Banderas

- `FLAG-1`: superior izquierda exterior.
- `FLAG-2`: superior izquierda interior.
- `FLAG-3`: superior derecha interior.
- `FLAG-4`: superior derecha exterior.
- `FLAG-5`: inferior izquierda; contenido girado 180°.
- `FLAG-6`: inferior derecha; contenido girado 180°.

Las seis son matrices 60×42, 2520 píxeles, divididas físicamente en tres bloques
de 14 filas con snake independiente. La posición se obtiene de `visual.slot` y
`configuration.visual_layout.flags.slots`.

## Colgantes del techo

- Izquierda: índices de layout 0–7, ruta `hangers-left`.
- Derecha: índices de layout 10–17, ruta `hangers-right`.
- Los índices 8–9 y 18–19 están reservados y no representan tiras activas.
- Ambos grupos tienen giro geométrico `RY = 180°` alrededor de sus pivotes.
- El giro del techo tiene `affects_signal_mapping = false`; no invertir la LUT.
- Después del giro, SC1 queda al frente y SC4 al fondo.

## Ondas laterales

- `WAVE-LEFT`: geometría normal, ruta `waves-left`.
- `WAVE-RIGHT`: geometría reflejada en X y contenido con `flip-x`.
- La escala del ensamblaje derecho permanece `[1,1,1]`; no aplicar una segunda
  escala X negativa.
- El cableado derecho sigue siendo `row-snake-100-right`.
- Usar la LUT evita el doble espejo entre geometría, contenido y snake.

## Árbol central

- Rejilla A: `A01–A12`; Rejilla B: `B01–B12`.
- Cada elemento incluye `tree_bottom`, `tree_top`, sentido y ruta.
- El inicio lógico vigente es A01/B01 según los nombres del controlador.
- `A09` y `B10` tienen 99 píxeles; ver la reserva explicada en el documento 2.

## Casa

- LED 1–47: ocultos, negros.
- LED 48–129: parante izquierdo desde abajo.
- LED 130–200: techo izquierdo.
- LED 201–271: techo derecho.
- LED 272–352: parante derecho hacia abajo.
- LED 353–400: ocultos, negros.

