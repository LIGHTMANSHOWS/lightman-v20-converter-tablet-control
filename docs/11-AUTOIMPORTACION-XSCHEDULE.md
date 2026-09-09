# Autoimportación segura de shows en xSchedule

Al iniciar LIGHTMAN V20 Minimal, el descubridor inspecciona únicamente las carpetas
directas de `%USERPROFILE%\Desktop\shows xlights`. Una carpeta se considera lista
para prueba interna cuando contiene un archivo `.fseq` y un audio compatible.
Los shows descubiertos nunca se publican automáticamente en el portal del cliente.

## Qué modifica

- El catálogo manual `ShowControl/shows.json` no se reescribe.
- Los descubrimientos se guardan en `ShowControl/shows.autodiscovered.json`.
- En `xlights.xschedule` sólo se añaden playlists que aún no existen.
- Las opciones, playlists, pasos y comentarios manuales existentes se conservan sin
  reserializarlos.
- Antes de cambiar el schedule se genera
  `xlights.xschedule.backup-auto-AAAAMMDD-HHMMSSmmm`.
- El reemplazo del XML se realiza de forma atómica y se cancela si otro proceso lo
  cambió durante la preparación.
- Un bloqueo lateral impide que dos instancias de V20 importen sobre el mismo
  schedule al mismo tiempo; la segunda deja su intento pendiente.

## Coordinación con xSchedule

V20 usa únicamente la API HTTP local documentada por xSchedule:

1. Consulta `GetPlayingStatus` y `GetPlayLists`.
2. Si el estado es `Playing` o `Paused`, no toca el archivo y deja el intento para el
   siguiente inicio.
3. Si está `Idle`, ejecuta `Save schedule` para no perder cambios hechos en la UI.
4. Añade las playlists nuevas y ejecuta `Change show folder` para recargar la carpeta.
5. Vuelve a consultar `GetPlayLists` y confirma que aparecieron.
6. Comprueba y, si hiciera falta, restaura el estado previo de `Output to Lights`.

Si la API de xSchedule no responde, V20 no modifica el archivo: no puede distinguir
con seguridad entre un programa cerrado y uno abierto con cambios sin guardar. El
informe pide abrir xSchedule y volver a iniciar V20. V20 nunca inicia, cierra, mata
ni reproduce nada en xSchedule como parte de esta sincronización.

## Casos omitidos

No se crea una playlist cuando falta FSEQ o audio, el FSEQ está truncado, los
archivos están fuera de su Show Folder, la carpeta no es hija directa de la raíz
configurada, hay varias parejas FSEQ/XSQ ambiguas, el nombre o el FSEQ ya están
representados, el XML no es válido, o xSchedule está reproduciendo.
Una carpeta incompleta permanece visible en el resumen interno para poder corregirla.

## Prueba aislada

La validación no usa el schedule real ni controla procesos. Crea carpetas temporales
y comprueba respaldo exacto, idempotencia, preservación byte a byte de playlists
manuales, rechazo de carpetas inválidas, bloqueo durante `Playing`/`Paused`, recarga
en `Idle` y comportamiento seguro cuando la recarga falla:

```powershell
dotnet run --project src\LightmanV20\ZapravkaStage3D.csproj -c Release -- --self-test-xschedule-sync
```
