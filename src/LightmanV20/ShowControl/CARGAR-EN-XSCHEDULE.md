# Cargar shows en xSchedule para LIGHTMAN V20

V20 no reproduce directamente el audio ni los `.fseq`. xSchedule reproduce cada
playlist y envía Art-Net a la entrada local de xLights de V20 (`127.0.0.3:6454`).

## Configuración

1. Abre xSchedule en la misma laptop donde corre V20.
2. Configura las salidas xLights del show para `127.0.0.3`, universos `0–124`,
   puerto UDP `6454`.
3. Activa `Output to lights` en xSchedule. En esta instalación “lights” significa
   V20 por loopback; xSchedule no debe apuntar al hardware.
4. Activa la API web local de xSchedule en el puerto indicado por `baseUrl` dentro
   de `shows.json` (por defecto `http://127.0.0.1:80/`).
5. Restringe el puerto 80 a localhost mediante la configuración de xSchedule o el
   Firewall de Windows. No expongas esa API a la LAN.

## Catálogo vigente

`shows.json` es la fuente de verdad y actualmente contiene doce shows habilitados
para el operador interno. Las playlists instaladas son:

1. `01 · SKEEWIFF`
2. `02 · SANTA EN ESPAÑOL`
3. `03 · SARAJEVO`
4. `04 · WINTER WIZARD`
5. `05 · MISER BROTHERS`
6. `06 · HERE COMES SANTA CLAUS`
7. `07 · SLEIGH RIDE 8-BIT`
8. `08 · THIS IS HALLOWEEN`
9. `09 · MY SONGS KNOW WHAT YOU DID IN THE DARK`
10. `10 · BABY SHARK EDM`
11. `12 · BELIEVER` — sólo prueba interna; audio aún no aprobado
12. `13 · UPTOWN FUNK` — sólo prueba interna; audio aún no aprobado

`11 · BLINDING LIGHTS` no se instala ni se habilita porque todavía no existe su
FSEQ. Las experiencias 12 y 13 permanecen como `Próximamente` en el portal del
cliente aunque el operador pueda probarlas desde el servidor interno.

Los nombres de las playlists deben coincidir exactamente con el campo `playlist`.
Las rutas usan `%USERPROFILE%` para no fijar un nombre de usuario; ajústalas en una
copia local si los shows se alojan en otro lugar. Los archivos `.fseq`, audio y
proyectos xLights no se guardan en este repositorio.

## Comportamiento desde la tablet

- Tocar un show ordena a xSchedule reproducir la playlist y selecciona `XLIGHTS`.
- Cambiar de `XLIGHTS` a `RESOLUME` o `TRACKING` pausa xSchedule.
- Activar el test general también pausa xSchedule.
- `PAUSAR / CONTINUAR` conserva la posición; `DETENER` corta la reproducción.
- La tablet solo se conecta a `http://IP-LAN-DE-V20:8780/`; nunca debe conectarse
  directamente a xSchedule.
