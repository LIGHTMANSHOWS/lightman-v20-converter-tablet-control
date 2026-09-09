# 1. Arquitectura y recepción Art-Net

## Flujo correcto

```text
Resolume / xLights / Motion Tracking
                 │ Art-Net RGB, universos 0–124
                 ▼
       LIGHTMAN V20 en la laptop puente
                 │
                 ├─ LMP3 → banderas Wi-Fi
                 └─ Art-Net → controladores Ethernet .91/.92/.93
```

El software de Motion Tracking no debe conocer ni controlar directamente las IP
físicas. Debe producir el mismo bloque de entrada que Resolume o xLights. La
tablet de V20 decide cuál fuente está activa.

## Destinos de recepción

| Fuente | Si corre en la misma laptop | Si corre en otra PC |
|---|---:|---:|
| Resolume | `127.0.0.2:6454` | No aplica |
| xLights | `127.0.0.3:6454` | No aplica |
| Tracking de prueba local | `127.0.0.4:6454` | No aplica |
| Motion Tracking por red | No usar loopback | IP Ethernet de la laptop V20, UDP `6454` |

En la revisión de este paquete la laptop V20 tiene Ethernet `192.168.1.6/24`, por
lo que el Motion Tracking conectado a esa red debe enviar unicast a
`192.168.1.6:6454`. Esta IP es una instantánea: el dato definitivo es la dirección
LAN que V20 muestra al abrirse.

`127.0.0.2`, `.3` y `.4` son direcciones loopback. Desde otra computadora nunca
llegan a V20.

## Formato ArtDMX aceptado

- Transporte: UDP.
- Puerto: `6454`.
- ID: bytes ASCII `Art-Net\0`.
- OpCode: ArtDMX `0x5000`, little-endian (`00 50`).
- Protocol version: 14.
- Universo: 15 bits, almacenado little-endian en bytes 14–15.
- Longitud DMX: big-endian en bytes 16–17, entre 2 y 512.
- Orden de color: RGB.
- Numeración de universo: base cero.
- Numeración de canal documentada: base uno.
- Cada universo transporta 170 píxeles RGB en canales 1–510.
- Los canales 511 y 512 permanecen sin usar.

## Comportamiento del receptor V20

- Escucha simultáneamente `127.0.0.2`, `127.0.0.3`, `127.0.0.4` y todas las IPv4
  activas de la laptop.
- Cada fuente se guarda por separado; no mezcla universos de dos emisores.
- Tracking remoto se identifica por la IP de origen del paquete.
- El modo automático bloquea una sola IP de tracking hasta que se rearma la
  selección. No hace failover ni mezcla por universo.
- Un universo se considera vigente durante 2 segundos.
- Si falta un fragmento de una ruta, V20 deja negra la ruta completa; esto evita
  mezclar un frame nuevo con información anterior.
- Solo se almacenan los universos declarados en el patch activo.

## Requisitos de red

- Tracking y V20 deben estar en la misma subred Ethernet `192.168.1.0/24`.
- Usar unicast; broadcast no es necesario.
- Permitir entrada UDP 6454 en el Firewall de Windows para la interfaz Ethernet.
- La red Ethernet aparece actualmente como perfil Público; la regla de firewall
  debe incluir ese perfil o cambiarse conscientemente a Privado.
- No abrir dos versiones de V20: la segunda no podrá reservar UDP 6454.

