# 3. Rutas actuales

## Entrada que debe generar el software fuente

| `route_id` | Píxeles | Inicio de entrada |
|---|---:|---:|
| `flag1` | 2520 | U0 C1 |
| `flag2` | 2520 | U15 C1 |
| `flag3` | 2520 | U30 C1 |
| `flag4` | 2520 | U45 C1 |
| `flag5` | 2520 | U60 C1 |
| `flag6` | 2520 | U75 C1 |
| `tree1` | 600 | U90 C1 |
| `tree2` | 600 | U94 C1 |
| `tree3` | 600 | U98 C1 |
| `tree4` | 600 | U102 C1 |
| `hangers-left` | 800 | U106 C1 |
| `hangers-right` | 800 | U111 C1 |
| `waves-left` | 400 | U116 C1 |
| `waves-right` | 400 | U119 C1 |
| `custom-casa-400` | 400 | U122 C1 |

Todos los inicios usan universo base cero y canal base uno. La continuación usa
170 píxeles por universo.

## Salida física que realiza V20

| Ruta | Destino físico |
|---|---|
| `flag1` | LMP3 `192.168.1.201:7777`, ID 1 |
| `flag2` | LMP3 `192.168.1.202:7777`, ID 2 |
| `flag3` | LMP3 `192.168.1.203:7777`, ID 3 |
| `flag4` | LMP3 `192.168.1.204:7777`, ID 4 |
| `flag5` | LMP3 `192.168.1.205:7777`, ID 5 |
| `flag6` | LMP3 `192.168.1.207:7777`, ID 7 |
| `tree1` | Art-Net `192.168.1.91`, U0 C1 |
| `tree2` | Art-Net `192.168.1.91`, U3 C271 |
| `tree3` | Art-Net `192.168.1.91`, U7 C31 |
| `tree4` | Art-Net `192.168.1.91`, U10 C301 |
| `hangers-left` | Art-Net `192.168.1.93`, U2 C181 |
| `hangers-right` | Art-Net `192.168.1.92`, U2 C181 |
| `waves-left` | Art-Net `192.168.1.93`, U0 C1 |
| `waves-right` | Art-Net `192.168.1.92`, U0 C1 |
| `custom-casa-400` | Art-Net `192.168.1.93`, U7 C31 |

Esta tabla física sirve para diagnóstico y para comprobar que V20 está configurado
igual. El software fuente remoto no debe usar estos destinos.

## Banderas por posición frontal

| Elemento | Posición | Ruta | Salida física |
|---|---|---|---|
| `FLAG-1` | superior izquierda exterior | `flag1` | `.201`, ID 1 |
| `FLAG-2` | superior izquierda interior | `flag2` | `.202`, ID 2 |
| `FLAG-3` | superior derecha interior | `flag3` | `.203`, ID 3 |
| `FLAG-4` | superior derecha exterior | `flag4` | `.204`, ID 4 |
| `FLAG-5` | inferior izquierda | `flag5` | `.205`, ID 5 |
| `FLAG-6` | inferior derecha | `flag6` | `.207`, ID 7 |

