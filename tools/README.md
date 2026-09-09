# Herramientas de mapeo

`build_v20_minimal_manifest.py` reconstruye el manifiesto V4 y el módulo web
`patch-map.js` usando como base el manifiesto vigente y la geometría del árbol de
`data/TREE-V33-A09-B10-99PX.json`.

```powershell
py -3 .\tools\build_v20_minimal_manifest.py
.\scripts\validate.ps1
```

El generador actualiza simultáneamente:

- `src/LightmanV20/Patch/SHOW-V20-MINIMAL.json`
- `integration/Data/SHOW-V20-MINIMAL-V4-PORTABLE.json`
- `src/LightmanV20/Web/patch-map.js`

No edites esas tres copias por separado. Antes de aceptar un cambio revisa la
geometría, las LUT, las rutas físicas y los sentinelas de orientación.

