# LIGHTMAN V20 · conversor, visualizador y control por tablet

Repositorio fuente del puente LIGHTMAN V20. Recibe una sola fuente Art-Net activa
(Resolume, xLights o Motion Tracking), muestra exactamente el cuadro seleccionado y
lo redistribuye a los controladores del montaje. V20 publica dos superficies web
separadas: una consola interna para el operador y una experiencia sencilla para el
cliente. Ambas se conectan únicamente a V20; xSchedule nunca se expone a la tablet.

> Estado de referencia: manifiesto V4
> `V20-MINIMAL-4-PORTABLE-VISUAL-BRIDGE`, actualizado el 8 de septiembre de 2026.

## Arquitectura

```text
Resolume 127.0.0.2 ─┐
xLights  127.0.0.3 ─┼─ Art-Net U0–U124 ─► V20 ─┬─ LMP3 ─► banderas Wi-Fi
Tracking por LAN ───┘                           └─ Art-Net ─► .91 / .92 / .93
                                      ▲
                         ┌────────────┴────────────┐
                         │                         │
              Operador HTTP :8780       Cliente HTTP :8781
              fuente / test / show       catálogo comercial / iniciar
                         │                         │
                         └──────────── V20 ────────┘
                                      │
                                      └─► xSchedule localhost :80
```

V20 es el único componente que conoce las salidas físicas. El software de Motion
Tracking debe producir el bloque lógico descrito por el manifiesto, enviarlo a la
IP Ethernet que V20 muestre al abrirse y no transmitir directamente a los
controladores.

## Contenido

- [`src/LightmanV20`](src/LightmanV20): aplicación WinForms .NET 8, puente,
  visualizador y servidor de tablet.
- [`src/LightmanV20/Patch/SHOW-V20-MINIMAL.json`](src/LightmanV20/Patch/SHOW-V20-MINIMAL.json):
  contrato autoritativo del mapeo y de la entrada Art-Net usado por el ejecutable.
- [`integration`](integration): contrato portátil, esquema JSON, adaptador de
  ejemplo y prueba independiente para software externo.
- [`docs`](docs): arquitectura, recepción, rutas, orientaciones, API de tablet y
  guía específica para Motion Tracking.
- [`tools/build_v20_minimal_manifest.py`](tools/build_v20_minimal_manifest.py):
  generador del manifiesto V4.

Los shows, audios, `.xsq` y `.fseq` no forman parte de este repositorio. Su catálogo
local está en `ShowControl/shows.json` y xSchedule los reproduce desde el equipo del
show.

## Compilar

Requisitos: Windows 10/11, SDK .NET 8 y WebView2 Runtime.

```powershell
dotnet restore .\src\LightmanV20\ZapravkaStage3D.csproj
dotnet build .\src\LightmanV20\ZapravkaStage3D.csproj -c Release
dotnet publish .\src\LightmanV20\ZapravkaStage3D.csproj -c Release -r win-x64 --self-contained true
```

El ejecutable publicado debe conservar junto a él las carpetas `Web`, `Patch` y
`ShowControl`.

## Validar

```powershell
py -3 .\integration\Tests\verify_package.py
node .\src\LightmanV20\tests\manifest-v4-orientation.test.mjs
dotnet run --project .\src\LightmanV20\ZapravkaStage3D.csproj -c Release -- --self-test-minimal
```

La validación no sustituye una prueba física. Mantén `Output to Lights` apagado
durante desarrollo y haz la primera prueba real con brillo limitado.

## Integración de Motion Tracking

Empieza por [`docs/07-MOTION-TRACKING.md`](docs/07-MOTION-TRACKING.md). Para evitar
los errores del mapeo antiguo, consume `element_id`, `route_id` y la LUT completa
`visual.pixel_mapping.route_pixel_indices`; no vuelvas a aplicar offsets, snake,
giros ni espejos.

Las dos interfaces y sus límites están documentados en
[`docs/06-TABLET-API.md`](docs/06-TABLET-API.md) y
[`docs/10-SERVIDOR-CLIENTE.md`](docs/10-SERVIDOR-CLIENTE.md). Los botones internos de modos de
tracking son una extensión planificada: el contrato de coordinación está en
[`docs/09-CONTRATO-MODOS-TRACKING.md`](docs/09-CONTRATO-MODOS-TRACKING.md), pero
todavía no se debe asumir que esos endpoints existen en V20.

## Alcance y seguridad

- Los servidores de operador y cliente están pensados únicamente para la LAN
  privada del montaje.
- La revisión actual no usa autenticación HTTP. El puerto 8781 reduce su superficie
  a catálogo comercial e inicio por ID, pero cualquier equipo de la LAN podría
  invocarlo. No expongas 8780 ni 8781 a Internet o a una red de invitados.
- No hay grabación de escenas ni persistencia de contenido.
- No abras dos copias de V20: ambas competirían por UDP 6454.
- No publiques ejecutables, audios, shows ni perfiles WebView2 en el repositorio.
- El código se entrega sin una licencia pública; conserva la titularidad del autor.
