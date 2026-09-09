# Paquete para integradores Art-Net

Esta carpeta permite integrar un generador externo —especialmente Motion
Tracking— sin copiar la lógica histórica de V20.

## Orden de lectura

1. [`../docs/01-ARQUITECTURA-Y-RECEPCION.md`](../docs/01-ARQUITECTURA-Y-RECEPCION.md)
2. [`../docs/02-COMO-INTERPRETAR-EL-MAPEO.md`](../docs/02-COMO-INTERPRETAR-EL-MAPEO.md)
3. [`../docs/03-RUTAS-ACTUALES.md`](../docs/03-RUTAS-ACTUALES.md)
4. [`../docs/04-ORIENTACIONES-VISUALES.md`](../docs/04-ORIENTACIONES-VISUALES.md)
5. [`../docs/07-MOTION-TRACKING.md`](../docs/07-MOTION-TRACKING.md)
6. [`Data/SHOW-V20-MINIMAL-V4-PORTABLE.json`](Data/SHOW-V20-MINIMAL-V4-PORTABLE.json)
7. [`Examples/manifest_adapter.py`](Examples/manifest_adapter.py)

## Contrato vigente

- Formato: `lightman-v20-show-manifest`
- Esquema: `4`
- Revisión: `V20-MINIMAL-4-PORTABLE-VISUAL-BRIDGE`
- Elementos: 52
- Píxeles visibles: 20 223
- Rutas: 15
- Entrada: universos Art-Net 0–124, RGB, 170 píxeles por universo

El software fuente toma universos y canales de `bridge_routes[].bridge_input`,
pero para Tracking remoto envía a la IP Ethernet real de V20. Los campos
`physical_output` solo describen lo que hará el puente posteriormente.

Valida el paquete con:

```powershell
py -3 .\integration\Tests\verify_package.py
```

