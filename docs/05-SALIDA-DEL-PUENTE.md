# 5. Salida del puente V20

Este documento es informativo. El software fuente solo envía Art-Net a V20.

## Art-Net hacia controladores Ethernet

V20 reconstruye buffers por ruta, los coloca desde el universo/canal físico
declarado en `physical_output` y envía datagramas UDP 6454 unicast. Mantiene 510
canales RGB útiles por universo y deja los dos últimos slots en cero.

Cuando una ruta desaparece o V20 se cierra, el motor intenta transmitir negro a
los destinos que estaban activos. Si ocurre un error de red, el modo automático
cierra los sockets y reintenta después de un segundo.

## LMP3 hacia banderas

Cada bandera recibe 2520 píxeles RGB. V20 convierte RGB888 a RGB565 y envía cuatro
datagramas UDP al puerto 7777.

Cabecera de cada datagrama:

| Offset | Tamaño | Contenido |
|---:|---:|---|
| 0 | 4 | ASCII `LMP3` |
| 4 | 1 | versión `1` |
| 5 | 1 | identificador de bandera |
| 6 | 2 | número de frame, little-endian |
| 8 | 2 | índice de fragmento, little-endian, 0–3 |
| 10 | 1 | cantidad total de fragmentos, `4` |
| 11 | 1 | reservado, `0` |
| 12 | 1 | fila inicial: 0, 12, 24 o 36 |
| 13 | 1 | cantidad de filas del fragmento: 12, 12, 12 o 6 |
| 14 | 2 | bytes de carga, little-endian |
| 16 | N | RGB565 big-endian |

La carga RGB565 completa tiene 5040 bytes. Los tres primeros fragmentos llevan
1440 bytes y el cuarto 720 bytes.

