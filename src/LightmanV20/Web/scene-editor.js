import { createNeonPixels } from './neon-pixels.js';
import { bindScrubNumber } from './scrub-number.js';
import { BANNER_OVERRIDES, mirrorTransform, WAVE_BASE_Y, WAVE_SPACING, PREVIOUS_WAVE_BASE_Y, PREVIOUS_WAVE_SPACING } from './banner-layout.js';
// Visual scene editing only: the receiver and the Resolume patch remain unchanged.
export function createSceneEditor({ THREE, scene, world, objects, state, renderer, camera, showNotice, initialOverrides = {}, initialDocument = null, readOnly = false }) {
  const $ = id => document.getElementById(id);
  const STORAGE = 'lightman-v20-arbol-ab-snake-waves-posts-1-5-v4';
  const entries = new Map();
  const assemblyIds = ['CENTRAL-TREE', 'CEILING-LEFT', 'CEILING-RIGHT'];
  const history = [];
  const extraGroup = new THREE.Group();
  world.add(extraGroup);
  const ambient = new THREE.AmbientLight('#cbdfff', 0.35);
  scene.add(ambient);
  const highlight = new THREE.Box3Helper(new THREE.Box3(), 0xffa044);
  highlight.material.depthTest = false;
  highlight.renderOrder = 100;
  scene.add(highlight);
  highlight.visible = false;
  let selected = null;
  let nextId = 1;
  let liveBefore = null;
  const fields = ['x', 'y', 'z', 'rx', 'ry', 'rz', 'count', 'spacing'];
  const status = text => { $('edit-status').textContent = text; };
  const finite = (value, low, high, integer = false) => {
    if (typeof value !== 'number' || !Number.isFinite(value) || value < low || value > high || (integer && !Number.isInteger(value))) {
      throw new Error(`Valor fuera de rango (${low}–${high}).`);
    }
    return value;
  };
  const color = value => {
    if (typeof value !== 'string' || !/^#[0-9a-f]{6}$/i.test(value)) throw new Error('Color no válido.');
    return value;
  };
  function register(object) {
    scene.updateMatrixWorld(true);
    const bounds = new THREE.Box3();
    object.members.forEach(member => bounds.expandByObject(member));
    const center = bounds.isEmpty() ? new THREE.Vector3() : bounds.getCenter(new THREE.Vector3());
    object.parent.worldToLocal(center);
    if (object.pivot) center.copy(object.pivot);
    const group = new THREE.Group();
    group.position.copy(center);
    object.parent.add(group);
    group.updateMatrixWorld(true);
    object.members.forEach(member => group.attach(member));
    group.userData.editorId = object.id;
    const entry = { ...object, group, copies: [], spec: object.spec || null,
      transform: { x: center.x, y: center.y, z: center.z, rx: 0, ry: 0, rz: 0, count: object.defaultCount ?? 1, spacing: 0.3, axis: 'X' } };
    group.visible = entry.transform.count > 0;
    entries.set(object.id, entry);
    return entry;
  }
  objects.forEach(register);

  function snapshot() {
    return { format: 'lightman-eth01-scene', version: 1, layoutRevision: 13,
      ambient: { intensity: ambient.intensity, color: `#${ambient.color.getHexString()}` },
      objects: [...entries.values()].map(e => ({ id: e.id, transform: { ...e.transform }, strip: e.spec ? { ...e.spec } : null })) };
  }
  for (const [id, changes] of Object.entries(initialOverrides)) {
    const entry = entries.get(id);
    if (entry) applyTransform(entry, { ...entry.transform, ...changes });
  }
  const original = snapshot();
  function save(notify = false) {
    try {
      localStorage.setItem(STORAGE, JSON.stringify(snapshot()));
      status('Diseño guardado en este visualizador. Exporta JSON para trasladarlo o conservar una copia.');
      if (notify) showNotice('Diseño guardado.');
    } catch { status('No se pudo guardar localmente. Usa Exportar JSON para conservar el diseño.'); }
  }
  function checkpoint() { history.push(snapshot()); if (history.length > 20) history.shift(); }
  function validateTransform(t) {
    if (!t || !['X', 'Y', 'Z'].includes(t.axis)) throw new Error('Transformación no válida.');
    for (const key of ['x', 'y', 'z']) finite(t[key], -50, 50);
    for (const key of ['rx', 'ry', 'rz']) finite(t[key], -360, 360);
    finite(t.count, 0, 16, true); finite(t.spacing, 0, 20);
  }
  function validateDocument(doc) {
    if (doc?.format !== 'lightman-eth01-scene' || doc.version !== 1 || !Array.isArray(doc.objects)) throw new Error('No es un diseño compatible con este editor.');
    finite(doc.ambient?.intensity, 0, 3); color(doc.ambient?.color);
    const baseIds = new Set(original.objects.map(e => e.id));
    const ids = new Set();
    let strips = 0, visualPixels = 0;
    if (doc.objects.length > original.objects.length + 32) throw new Error('Máximo 32 strips nuevos.');
    for (const object of doc.objects) {
      if (typeof object.id !== 'string' || ids.has(object.id)) throw new Error('Identificadores duplicados o inválidos.');
      ids.add(object.id); validateTransform(object.transform);
      if (object.strip) {
        if (!/^STRIP-\d+$/.test(object.id) || baseIds.has(object.id)) throw new Error('Identificador de strip no válido.');
        finite(object.strip.length, 0.1, 20); finite(object.strip.pixels, 2, 1000, true); color(object.strip.color);
        strips++;
        visualPixels += object.strip.pixels * object.transform.count;
      } else {
        if (!baseIds.delete(object.id)) throw new Error('Objeto base desconocido.');
        const entry = entries.get(object.id);
        const assemblyCount = entry?.selectionGroup === 'CEILING-RESERVE' ? 0 : doc.objects.find(o => o.id === entry?.selectionGroup)?.transform?.count ?? 1;
        visualPixels += (entry?.record?.element.pixel_count || 0) * object.transform.count * assemblyCount;
      }
    }
    // Older exports contain the 24 individual strands but not their new parent.
    if (!doc.layoutRevision || doc.layoutRevision < 5) baseIds.delete('CENTRAL-TREE');
    if (!doc.layoutRevision || doc.layoutRevision < 7) { baseIds.delete('CEILING-LEFT'); baseIds.delete('CEILING-RIGHT'); }
    if (baseIds.size || strips > 32 || visualPixels > 150000) throw new Error('Diseño incompleto o supera 150.000 píxeles visuales.');
  }
  function applyTransform(entry, t) {
    const wanted = Math.max(0, t.count - 1);
    while (entry.copies.length > wanted) {
      const removed = entry.copies.pop();
      removed.traverse(node => { if (node.isInstancedMesh) node.dispose(); });
      removed.removeFromParent();
    }
    entry.transform = { ...t };
    entry.group.position.set(t.x, t.y, t.z);
    entry.group.rotation.set(...[t.rx, t.ry, t.rz].map(THREE.MathUtils.degToRad));
    entry.group.visible = t.count > 0;
    for (let i = entry.copies.length + 1; i < t.count; i++) {
      // Geometry/color buffers are intentionally shared: a copy is a signal mirror, not a new patch.
      const copy = entry.group.clone(true);
      const sourceInstances = [];
      entry.group.traverse(node => { if (node.isInstancedMesh) sourceInstances.push(node); });
      let instanceIndex = 0;
      copy.traverse(node => { if (node.isInstancedMesh) node.instanceColor = sourceInstances[instanceIndex++].instanceColor; });
      entry.parent.add(copy);
      entry.copies.push(copy);
    }
    entry.copies.forEach((copy, index) => {
      copy.position.copy(entry.group.position);
      copy.rotation.copy(entry.group.rotation);
      copy.visible = true;
      copy.position[t.axis.toLowerCase()] += (index + 1) * t.spacing;
    });
    scene.updateMatrixWorld(true);
  }
  function makeStrip(id, spec) {
    const { mesh } = createNeonPixels([new THREE.Vector3(), new THREE.Vector3(0, spec.length, 0)], spec.pixels);
    const material = mesh.material;
    material.color.set(spec.color);
    extraGroup.add(mesh);
    const entry = register({ id, label: `${id} · ${spec.length} m / ${spec.pixels} px · SIN PATCH`, parent: extraGroup, members: [mesh], spec });
    entry.stripMaterial = material;
    nextId = Math.max(nextId, Number(id.split('-')[1]) + 1);
    return entry;
  }
  function removeStrip(entry) {
    entry.copies.forEach(copy => copy.removeFromParent());
    entry.group.removeFromParent();
    entry.members[0].geometry.dispose();
    entry.stripMaterial.dispose();
    entries.delete(entry.id);
  }
  function refreshList() {
    $('edit-object').replaceChildren(...[...entries.values()].filter(entry => !entry.selectionGroup).map(entry => {
      const option = document.createElement('option'); option.value = entry.id;
      option.textContent = entry.label; return option;
    }));
  }
  function select(id) {
    id = entries.get(id)?.selectionGroup || id;
    const entry = entries.get(id);
    if (!entry) return;
    selected = id; $('edit-object').value = id;
    fields.forEach(key => { $('edit-' + key).value = Number(entry.transform[key].toFixed(4)); });
    $('edit-axis').value = entry.transform.axis;
    $('edit-angle').value = entry.transform[$('edit-rotation-axis').value];
    $('edit-info').textContent = entry.record
      ? `${entry.id} · ${entry.record.element.target_ip} · ${entry.record.element.pixel_count} px. Copias = misma señal.`
      : entry.id === 'CENTRAL-TREE' ? 'Árbol completo · rejillas 101/102 de 12 tiras · 600+600 px por controlador · snake.'
      : ['CEILING-LEFT', 'CEILING-RIGHT'].includes(entry.id) ? '8 tiras × 5 m / 100 px · 4 anclajes / 3 caídas · escalón 9.80 cm · RY base 180°.'
      : entry.spec ? 'SIN PATCH · prueba local. No incluido en el CSV ni en Resolume.' : 'Objeto físico / NDI · sin canales LED.';
  }
  function restore(doc) {
    validateDocument(doc);
    [...entries.values()].filter(e => e.spec).forEach(removeStrip);
    const assemblies = assemblyIds.map(id => entries.get(id)).filter(Boolean);
    // Remove stale group copies before restoring any inner strand transforms.
    assemblies.forEach(group => applyTransform(group, { ...group.transform, count: 1 }));
    for (const object of doc.objects.filter(o => !assemblyIds.includes(o.id))) {
      const entry = object.strip ? makeStrip(object.id, object.strip) : entries.get(object.id);
      applyTransform(entry, object.transform);
    }
    assemblies.forEach(group => applyTransform(group, (doc.objects.find(o => o.id === group.id) || original.objects.find(o => o.id === group.id)).transform));
    ambient.intensity = doc.ambient.intensity; ambient.color.set(doc.ambient.color);
    $('ambient-intensity').value = ambient.intensity * 100;
    $('ambient-value').value = `${Math.round(ambient.intensity * 100)}%`;
    $('ambient-color').value = doc.ambient.color;
    refreshList(); select(entries.has(selected) ? selected : entries.keys().next().value);
  }
  function attempt(action) { try { action(); } catch (error) { status(error.message); showNotice(error.message); } }
  function liveChange(key, value) {
    const entry = entries.get(selected);
    if (entry.transform[key] === value) return true;
    const t = { ...entry.transform, [key]: value };
    try {
      validateTransform(t);
      if (key === 'count') { const doc = snapshot(); doc.objects.find(o => o.id === selected).transform = t; validateDocument(doc); }
    } catch (error) { status(error.message); return false; }
    liveBefore ||= snapshot();
    applyTransform(entry, t);
    if (key === $('edit-rotation-axis').value) $('edit-angle').value = value;
    status('Edición en vivo · suelta para guardar · Esc cancela el ajuste.');
    return true;
  }
  function finishLive() {
    if (liveBefore) {
      history.push(liveBefore); if (history.length > 20) history.shift(); liveBefore = null; save();
    }
    select(selected);
  }
  function cancelLive() {
    if (liveBefore) { const before = liveBefore; liveBefore = null; restore(before); }
    select(selected);
  }
  for (const key of fields) {
    const input = $('edit-' + key);
    if (key === 'count') input.dataset.integer = 'true';
    bindScrubNumber(input, { rate: key === 'count' ? 1 / 12 : key.startsWith('r') ? 0.5 : 0.01,
      change: value => liveChange(key, value), finish: finishLive, cancel: cancelLive });
  }
  bindScrubNumber($('edit-angle'), { rate: 0.5,
    change: value => { const key = $('edit-rotation-axis').value; const ok = liveChange(key, value); if (ok) $('edit-' + key).value = value; return ok; },
    finish: finishLive, cancel: cancelLive });
  for (const id of ['strip-length', 'strip-pixels']) {
    const input = $(id);
    if (id === 'strip-pixels') input.dataset.integer = 'true';
    let start = input.value;
    input.addEventListener('focus', () => { start = input.value; });
    bindScrubNumber(input, { rate: id === 'strip-pixels' ? 0.25 : 0.01,
      change: value => value >= Number(input.min) && value <= Number(input.max),
      finish: () => { start = input.value; }, cancel: () => { input.value = start; } });
  }
  $('edit-rotation-axis').addEventListener('change', () => { finishLive(); select(selected); });
  $('edit-axis').addEventListener('change', () => { liveChange('axis', $('edit-axis').value); finishLive(); });
  $('edit-object').addEventListener('change', event => select(event.target.value));
  $('edit-apply').addEventListener('click', () => attempt(() => {
    const entry = entries.get(selected);
    const t = Object.fromEntries(fields.map(key => {
      const value = $('edit-' + key).value;
      if (value.trim() === '') throw new Error('Completa todos los campos.');
      return [key, Number(value)];
    }));
    t.axis = $('edit-axis').value; validateTransform(t);
    const doc = snapshot(); doc.objects.find(e => e.id === selected).transform = t; validateDocument(doc);
    finishLive(); save();
  }));
  $('strip-add').addEventListener('click', () => attempt(() => {
    const spec = { length: Number($('strip-length').value), pixels: Number($('strip-pixels').value), color: $('strip-color').value };
    finite(spec.length, 0.1, 20); finite(spec.pixels, 2, 1000, true); color(spec.color);
    if ([...entries.values()].filter(e => e.spec).length >= 32) throw new Error('Máximo 32 strips nuevos.');
    checkpoint(); const id = `STRIP-${nextId++}`; makeStrip(id, spec);
    refreshList(); select(id); save(); showNotice('Strip añadido sin patch. Activa Simulación local para probarlo.');
  }));
  $('edit-undo').addEventListener('click', () => attempt(() => {
    if (!history.length) return status('No hay cambios para deshacer.');
    restore(history.pop()); save();
  }));
  for (const id of ['ambient-intensity', 'ambient-color']) {
    $(id).addEventListener('input', () => {
      liveBefore ||= snapshot(); ambient.intensity = Number($('ambient-intensity').value) / 100;
      ambient.color.set($('ambient-color').value);
      $('ambient-value').value = `${$('ambient-intensity').value}%`;
    });
    $(id).addEventListener('change', finishLive);
  }
  $('edit-save').addEventListener('click', () => save(true));
  $('edit-export').addEventListener('click', () => {
    if (location.origin === 'http://127.0.0.1:8765') {
      fetch('http://127.0.0.1:8766/scene', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(snapshot()) })
        .then(r => { if (r.ok) showNotice('Diseño capturado para compilar.'); }).catch(() => {});
    }
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob([JSON.stringify(snapshot(), null, 2)], { type: 'application/json' }));
    link.download = 'Lightman-Diseno-Editor-v8.json'; link.click();
    setTimeout(() => URL.revokeObjectURL(link.href), 1000);
  });
  $('edit-import').addEventListener('click', () => $('edit-file').click());
  $('edit-file').addEventListener('change', async event => {
    const file = event.target.files[0]; if (!file) return;
    try {
      if (file.size > 1024 * 1024) throw new Error('El JSON debe ocupar menos de 1 MB.');
      const doc = JSON.parse(await file.text()); validateDocument(doc);
      checkpoint(); restore(doc); save(); showNotice('Diseño importado. Patch Art-Net original conservado.');
    } catch (error) { status(`No se importó: ${error.message}`); }
    event.target.value = '';
  });
  $('edit-reset').addEventListener('click', () => attempt(() => { checkpoint(); restore(original); save(); showNotice('Diseño original restaurado. Puedes deshacer.'); }));
  refreshList(); select(entries.keys().next().value);
  try {
    const stored = (!readOnly && localStorage.getItem(STORAGE)) || (initialDocument && JSON.stringify(initialDocument));
    if (stored) {
      const doc = JSON.parse(stored);
      validateDocument(doc);
      if (!doc.layoutRevision || doc.layoutRevision < 9) {
        localStorage.setItem(STORAGE + '-before-four-anchors-9', stored);
        // Only the per-strip shape changes; retain all assembly positions and rotations.
        for (const object of doc.objects.filter(o => /^CEILING-\d+$/.test(o.id))) {
          object.transform = { ...original.objects.find(o => o.id === object.id).transform };
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 8) {
        localStorage.setItem(STORAGE + '-before-roof-stagger-8', stored);
        for (const object of doc.objects) {
          if (/^CEILING-\d+$/.test(object.id)) object.transform = { ...original.objects.find(o => o.id === object.id).transform };
          if (['CEILING-LEFT', 'CEILING-RIGHT'].includes(object.id)) object.transform.ry = 180;
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 7) {
        localStorage.setItem(STORAGE + '-before-ceiling-7', stored);
        // The user requested a new ceiling design; retain every non-ceiling object.
        for (const object of doc.objects.filter(o => /^CEILING-\d+$/.test(o.id))) {
          object.transform = { ...original.objects.find(o => o.id === object.id).transform };
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 6) {
        localStorage.setItem(STORAGE + '-before-m-wave-6', stored);
      }
      if (!doc.layoutRevision || doc.layoutRevision < 5) {
        localStorage.setItem(STORAGE + '-before-tree-group-5', stored);
      }
      if (!doc.layoutRevision || doc.layoutRevision < 4) {
        localStorage.setItem(STORAGE + '-before-banners-4', stored);
      }
      // One-time targeted correction; retain the user's positions and all other objects.
      if (!doc.layoutRevision || doc.layoutRevision < 3) {
        for (const object of doc.objects) if (object.id.startsWith('FLAG-')) {
          object.transform.rx = 0;
          object.transform.ry = 0;
          object.transform.rz = 0;
          if (['FLAG-1', 'FLAG-4'].includes(object.id)) object.transform.y = 0.85;
          if (['FLAG-2', 'FLAG-5'].includes(object.id)) object.transform.y = 2.75;
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 4) {
        for (let i = 1; i <= 3; i++) {
          const left = doc.objects.find(o => o.id === `FLAG-${i}`);
          doc.objects.find(o => o.id === `FLAG-${i + 3}`).transform = mirrorTransform(left.transform);
        }
        for (const object of doc.objects) {
          if (BANNER_OVERRIDES[object.id]) Object.assign(object.transform, BANNER_OVERRIDES[object.id]);
          if (object.id === 'TV-L') object.transform.count = 1;
          if (object.id === 'TV-R') object.transform.count = 0;
        }
      }
      if (doc.layoutRevision >= 4 && doc.layoutRevision < 6) {
        for (const object of doc.objects.filter(o => ['WAVE-LEFT', 'WAVE-RIGHT'].includes(o.id))) {
          object.transform.y += WAVE_BASE_Y - PREVIOUS_WAVE_BASE_Y;
          if (object.transform.axis === 'Y') object.transform.spacing = Math.max(0, object.transform.spacing + WAVE_SPACING - PREVIOUS_WAVE_SPACING);
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 10) {
        localStorage.setItem(STORAGE + '-before-reduced-layout-10', stored);
        for (const object of doc.objects) {
          if (/^CEILING-\d+$/.test(object.id)) object.transform = { ...original.objects.find(o => o.id === object.id).transform };
          if (['WAVE-LEFT', 'WAVE-RIGHT'].includes(object.id)) {
            object.transform.count = 4;
            object.transform.spacing = WAVE_SPACING;
            object.transform.axis = 'Y';
          }
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 11) {
        localStorage.setItem(STORAGE + '-before-pixel-patch-11', stored);
        for (const object of doc.objects.filter(o => ['WAVE-LEFT', 'WAVE-RIGHT'].includes(o.id))) {
          object.transform.count = object.transform.count ? 1 : 0;
        }
      }
      if (!doc.layoutRevision || doc.layoutRevision < 13) {
        localStorage.setItem(STORAGE + '-before-sc-supports-13', stored);
        // Shape/bounds changed. Refresh only the hidden strip pivots, keeping
        // assembly positions and every non-ceiling object exactly as saved.
        for (const object of doc.objects.filter(o => /^CEILING-\d+$/.test(o.id))) {
          object.transform = { ...original.objects.find(o => o.id === object.id).transform };
        }
      }
      restore(doc); save();
    }
  }
  catch { status('El diseño guardado no es compatible. Se mantiene la escena original.'); }
  const raycaster = new THREE.Raycaster(); raycaster.params.Points.threshold = 0.04;
  let down;
  if (!readOnly) {
    renderer.domElement.addEventListener('pointerdown', e => { down = [e.clientX, e.clientY]; });
    renderer.domElement.addEventListener('pointerup', e => {
      if (!down || Math.hypot(e.clientX - down[0], e.clientY - down[1]) > 5) return;
      const rect = renderer.domElement.getBoundingClientRect();
      raycaster.setFromCamera(new THREE.Vector2((e.clientX - rect.left) / rect.width * 2 - 1, 1 - (e.clientY - rect.top) / rect.height * 2), camera);
      for (const hit of raycaster.intersectObject(world, true)) {
        let node = hit.object, id = null, visible = true;
        while (node) { visible &&= node.visible; id ||= node.userData.editorId; node = node.parent; }
        if (visible && id) { select(id); break; }
      }
    });
  }
  function update() {
    for (const item of entries.values()) if (item.isLayerVisible) {
      item.group.visible = item.transform.count > 0 && item.isLayerVisible();
      item.copies.forEach(copy => { copy.visible = item.isLayerVisible(); });
    }
    const entry = entries.get(selected);
    let visible = !!entry && entry.transform.count > 0;
    for (let parent = entry?.group; parent; parent = parent.parent) visible &&= parent.visible;
    highlight.visible = !readOnly && visible;
    if (!readOnly && visible) { highlight.box.setFromObject(entry.group); highlight.updateMatrixWorld(true); }
    for (const item of entries.values()) if (item.spec) {
      const intensity = state.blackout ? 0 : state.mode === 'simulation' ? state.brightness : 0.03;
      item.stripMaterial.color.set(item.spec.color).multiplyScalar(intensity);
    }
  }
  return { select, update, snapshot };
}
