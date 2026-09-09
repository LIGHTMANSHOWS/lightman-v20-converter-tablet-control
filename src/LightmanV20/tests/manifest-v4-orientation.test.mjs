import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const manifestPath = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.join(project, 'Patch', 'SHOW-V20-MINIMAL.json');
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));

// Import the real browser mapping implementation even though the project does
// not declare all .js files as ESM for Node.
const pixelMapSource = await readFile(path.join(project, 'Web', 'pixel-map.js'), 'utf8');
const pixelMapUrl = `data:text/javascript;base64,${Buffer.from(pixelMapSource).toString('base64')}`;
const { sourcePixelFor } = await import(pixelMapUrl);

assert.equal(manifest.format, 'lightman-v20-show-manifest');
assert.equal(manifest.schema_version, 4);
assert.equal(manifest.configuration.manifest_schema_version, 4);
assert.equal(manifest.configuration.visual_layout.schema_version, 4);

const layout = manifest.configuration.visual_layout;
assert.deepEqual(layout.coordinate_system, {
  units: 'meters',
  handedness: 'right-handed',
  axes: { x: 'right', y: 'up', z: 'front-toward-audience' },
  origin: 'floor-center of the house outline',
  angles: 'degrees',
  euler_order: 'XYZ'
});

const elements = manifest.elements;
for (const element of elements) {
  assert.ok(element.visual, `${element.element_id}: falta visual`);
  assert.ok(element.bridge_input, `${element.element_id}: falta bridge_input`);
  assert.ok(element.physical_output, `${element.element_id}: falta physical_output`);
  const mapping = element.visual.pixel_mapping;
  assert.equal(mapping.route_id, element.route_id, `${element.element_id}: route_id visual`);
  assert.equal(mapping.route_offset_pixels, element.route_offset, `${element.element_id}: route_offset visual`);
  assert.equal(mapping.route_pixel_indices.length, element.pixel_count, `${element.element_id}: longitud del mapa`);
  const expected = Array.from({ length: element.pixel_count }, (_, visualPixel) =>
    element.route_offset + sourcePixelFor(element, visualPixel));
  assert.deepEqual(mapping.route_pixel_indices, expected, `${element.element_id}: V4 difiere de pixel-map.js`);
  assert.deepEqual([...new Set(expected)].sort((a, b) => a - b),
    Array.from({ length: element.pixel_count }, (_, i) => element.route_offset + i),
    `${element.element_id}: mapa incompleto o duplicado`);
}

const flags = elements.filter(element => element.kind === 'flag');
const expectedFlags = [
  ['FLAG-1', 'flag1', 'upper-left-outer', false],
  ['FLAG-2', 'flag2', 'upper-left-inner', false],
  ['FLAG-3', 'flag3', 'upper-right-inner', false],
  ['FLAG-4', 'flag4', 'upper-right-outer', false],
  ['FLAG-5', 'flag5', 'lower-left', true],
  ['FLAG-6', 'flag6', 'lower-right', true]
];
assert.deepEqual(flags.map(flag => [flag.element_id, flag.route_id, flag.visual_slot, flag.visual_rotate_180]), expectedFlags);
for (const flag of flags) {
  const slot = layout.flags.slots[flag.visual_slot];
  assert.ok(slot, `${flag.element_id}: slot no resuelto`);
  assert.deepEqual(flag.visual.placement, slot, `${flag.element_id}: placement no autocontenido`);
  assert.equal(flag.visual.slot_ref, `#/configuration/visual_layout/flags/slots/${flag.visual_slot}`);
  assert.deepEqual(flag.visual.pixel_mapping.visual_pixel_transform,
    flag.visual_rotate_180 ? [{ operation: 'rotate-180', width: 60, height: 42 }] : []);
}
assert.deepEqual(layout.flags.slots['upper-left-inner'].assembly_transform.position_m.map(Math.abs),
  layout.flags.slots['upper-right-inner'].assembly_transform.position_m.map(Math.abs));
assert.deepEqual(layout.flags.slots['lower-left'].assembly_transform.position_m.map(Math.abs),
  layout.flags.slots['lower-right'].assembly_transform.position_m.map(Math.abs));

const ceilings = elements.filter(element => element.kind === 'ceiling');
assert.equal(ceilings.length, 16);
for (const [id, pivot] of [['CEILING-LEFT', [-2.1, 2.7023936175515635, 0]], ['CEILING-RIGHT', [2.1, 2.7023936175515635, 0]]]) {
  const group = layout.ceiling.groups[id];
  assert.deepEqual(group.pivot_m, pivot);
  assert.deepEqual(group.transform.rotation_deg_xyz, [0, 180, 0]);
  assert.equal(group.affects_signal_mapping, false);
}
for (const ceiling of ceilings) {
  const expectedGroup = ceiling.layout_index < 10 ? 'CEILING-LEFT' : 'CEILING-RIGHT';
  assert.equal(ceiling.visual.group, expectedGroup, `${ceiling.element_id}: grupo de techo`);
  assert.equal(ceiling.visual.group_ref, `#/configuration/visual_layout/ceiling/groups/${expectedGroup}`);
  assert.deepEqual(ceiling.visual.pixel_mapping.visual_pixel_transform, [],
    `${ceiling.element_id}: RY180 no debe invertir señal`);
}

const waveLeft = elements.find(element => element.element_id === 'WAVE-LEFT');
const waveRight = elements.find(element => element.element_id === 'WAVE-RIGHT');
assert.equal(layout.waves.groups['WAVE-LEFT'].geometry_transform.mirror_axis, null);
assert.equal(layout.waves.groups['WAVE-RIGHT'].geometry_transform.mirror_axis, 'X');
assert.equal(layout.waves.groups['WAVE-RIGHT'].mirror_of, 'WAVE-LEFT');
assert.deepEqual(layout.waves.groups['WAVE-RIGHT'].assembly_transform.scale_xyz, [1, 1, 1],
  'el espejo ya está en geometry_transform; una escala X negativa lo aplicaría dos veces');
assert.equal(waveLeft.visual.geometry_transform.mirror_axis, null);
assert.equal(waveRight.visual.geometry_transform.mirror_axis, 'X');
assert.deepEqual(waveLeft.visual.pixel_mapping.visual_pixel_transform, []);
assert.deepEqual(waveRight.visual.pixel_mapping.visual_pixel_transform,
  [{ operation: 'flip-x', row_width: 100, rows: 4 }]);
assert.deepEqual(waveRight.visual.pixel_mapping.route_pixel_indices,
  waveLeft.visual.pixel_mapping.route_pixel_indices,
  'mirror-X + flip-X debe conservar el contenido por posición equivalente');

const routes = new Map(manifest.configuration.bridge_routes.map(route => [route.route_id, route]));
assert.equal(routes.size, 15);
assert.deepEqual([...routes.keys()], [
  'flag1', 'flag2', 'flag3', 'flag4', 'flag5', 'flag6',
  'tree1', 'tree2', 'tree3', 'tree4',
  'hangers-left', 'hangers-right', 'waves-left', 'waves-right', 'custom-casa-400'
]);
assert.deepEqual(['flag1', 'flag2', 'flag3', 'flag4', 'flag5', 'flag6'].map(id => {
  const route = routes.get(id);
  return [id, route.bridge_input.universe, route.physical_output.ip, route.physical_output.port, route.physical_output.ident];
}), [
  ['flag1', 0, '192.168.1.201', 7777, 1],
  ['flag2', 15, '192.168.1.202', 7777, 2],
  ['flag3', 30, '192.168.1.203', 7777, 3],
  ['flag4', 45, '192.168.1.204', 7777, 4],
  ['flag5', 60, '192.168.1.205', 7777, 5],
  ['flag6', 75, '192.168.1.207', 7777, 7]
]);

console.log(`PASS manifest-v4-orientation · ${elements.length} elementos · ${routes.size} rutas`);
