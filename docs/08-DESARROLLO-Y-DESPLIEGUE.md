# 8. Desarrollo y despliegue

## Fuente de verdad

1. `src/LightmanV20/Patch/SHOW-V20-MINIMAL.json` es el contrato autoritativo que
   carga el receptor y que se incrusta en el visualizador mediante `patch-map.js`.
2. `integration/Data/SHOW-V20-MINIMAL-V4-PORTABLE.json` es la copia entregada a
   integradores externos.
3. Ambas copias deben ser idénticas byte por byte antes de publicar una revisión.
4. Las rutas físicas siguen declaradas en `LiveEngine.DefaultRoutes`; el self-test
   comprueba que coincidan con `physical_output` del manifiesto.
5. Un cambio de mapeo requiere actualizar el `mapping_revision`, regenerar las
   pruebas sentinela y anotar el cambio en `CHANGELOG.md`.

## Compilación segura

```powershell
.\scripts\validate.ps1
.\scripts\build.ps1
```

Los scripts no activan salidas físicas. El self-test usa memoria o loopback.

## Publicación

- No confirmes `bin`, `obj`, perfiles WebView2 ni configuraciones de usuario.
- No publiques `.fseq`, `.xsq`, audio o vídeo de shows.
- Genera el portable con `dotnet publish` y distribúyelo fuera del historial Git,
  por ejemplo como un artefacto de release cuando corresponda.
- Conserva `Web`, `Patch` y `ShowControl` junto al ejecutable publicado.

## Lista antes de integrar un cambio

- [ ] Compila sin errores.
- [ ] Pasa el self-test C#.
- [ ] Pasa el test de orientación JavaScript.
- [ ] Pasa el verificador independiente del paquete.
- [ ] Las dos copias del manifiesto tienen el mismo SHA-256.
- [ ] La tablet sigue mostrando estado real, no estado supuesto.
- [ ] Resolume, xLights y Tracking permanecen aislados.
- [ ] El cambio no añade un destino físico al software fuente.
- [ ] La prueba física se realizó aparte y con brillo limitado.
