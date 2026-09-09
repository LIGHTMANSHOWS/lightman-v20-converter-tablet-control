import * as THREE from './three.module.min.js';
import { OrbitControls } from './OrbitControls.js';
import { ETH01_PATCH } from './patch-map.js?revision=v20-minimal-4-portable-visual-bridge';
import { createSceneEditor } from './scene-editor.js';
import { CEILING_LAYOUT, CEILING_SUPPORTS, CEILING_PIVOT_Y, ceilingPath, ceilingAnchors, ceilingRoofRise, ceilingPlacement } from './ceiling-layout.js';
import { BANNER, WAVE_PATH, WAVE_SPACING, BANNER_PATHS, CURRENT_LAYOUT, FLAG_SLOT_TRANSFORMS } from './banner-layout.js?revision=v20-minimal-3-orientacion-visual';
import { createNeonPixels, createNeonRuns } from './neon-pixels.js';
import { sourcePixelFor } from './pixel-map.js?revision=v20-minimal-4-portable-visual-bridge';

const PATCH = ETH01_PATCH;
const CONFIG = PATCH.configuration;
const ELEMENTS = PATCH.elements;
const VISUAL_LAYOUT = CONFIG.visual_layout?.schema_version >= 4 ? CONFIG.visual_layout : null;
const MINIMAL_VIEW = true;
const CHANNELS_PER_PIXEL = CONFIG.channels_per_pixel;
const PIXELS_PER_UNIVERSE = CONFIG.pixels_per_universe;

function numericTriple(value) {
  if (!Array.isArray(value) || value.length !== 3) return null;
  const numbers = value.map(Number);
  return numbers.every(Number.isFinite) ? numbers : null;
}

function sceneTransform(portable, fallback = {}) {
  const result = { ...fallback };
  const position = numericTriple(portable?.position_m);
  const rotation = numericTriple(portable?.rotation_deg_xyz);
  if (position) [result.x, result.y, result.z] = position;
  if (rotation) [result.rx, result.ry, result.rz] = rotation;
  return result;
}

const ui = {
  viewport: document.getElementById('viewport'),
  loading: document.getElementById('loading'),
  mode: document.getElementById('mode'),
  artNetInput: document.getElementById('artnet-input'),
  artNetInputIp: document.getElementById('artnet-input-ip'),
  artNetInputApply: document.getElementById('artnet-input-apply'),
  artNetInputStatus: document.getElementById('artnet-input-status'),
  artNetSenders: document.getElementById('artnet-senders'),
  receiverAddresses: document.getElementById('receiver-addresses'),
  modeDot: document.getElementById('mode-dot'),
  modeLabel: document.getElementById('mode-label'),
  modeHelp: document.getElementById('mode-help'),
  networkBadge: document.getElementById('network-badge'),
  artNetSignal: document.getElementById('artnet-signal'),
  artNetSource: document.getElementById('artnet-source'),
  exportPatch: document.getElementById('export-patch'),
  installResolume: document.getElementById('install-resolume'),
  installStatus: document.getElementById('install-status'),
  ndiSource: document.getElementById('ndi-source'),
  ndiRefresh: document.getElementById('ndi-refresh'),
  ndiStatus: document.getElementById('ndi-status'),
  effect: document.getElementById('effect'),
  play: document.getElementById('play'),
  playState: document.getElementById('play-state'),
  blackout: document.getElementById('blackout'),
  brightness: document.getElementById('brightness'),
  brightnessValue: document.getElementById('brightness-value'),
  speed: document.getElementById('speed'),
  speedValue: document.getElementById('speed-value'),
  colorA: document.getElementById('color-a'),
  colorB: document.getElementById('color-b'),
  showFlags: document.getElementById('show-flags'),
  showCentral: document.getElementById('show-central'),
  showCeiling: document.getElementById('show-ceiling'),
  showOutline: document.getElementById('show-outline'),
  showTvs: document.getElementById('show-tvs'),
  showStructure: document.getElementById('show-structure'),
  selection: document.querySelector('#selection strong'),
  notice: document.getElementById('notice')
};

const state = {
  mode: 'artnet',
  effect: 'weave',
  playing: true,
  blackout: false,
  brightness: 1,
  speed: 1,
  colorA: new THREE.Color('#c8ffb0'),
  colorB: new THREE.Color('#31f0ff'),
  elapsed: 0,
  selected: null
};

const artNetState = {
  input: 'local',
  pendingInput: null,
  inputFilters: { local: '', unity: '' },
  colors: null,
  packets: 0,
  packetRate: 0,
  universes: 0,
  expectedUniverses: CONFIG.used_universes,
  lastPacketMs: -1,
  sources: [],
  receiverError: null
};
artNetState.routeColors = {};
artNetState.outputSignal = false;

const renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'high-performance' });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.75));
renderer.setSize(ui.viewport.clientWidth, ui.viewport.clientHeight);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.08;
ui.viewport.appendChild(renderer.domElement);

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x050b14);
scene.fog = new THREE.FogExp2(0x050b14, 0.026);

const camera = new THREE.PerspectiveCamera(
  41,
  ui.viewport.clientWidth / Math.max(1, ui.viewport.clientHeight),
  0.04,
  80
);
camera.position.set(0, 2.65, 9.4);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 2.0, -0.08);
controls.enableDamping = true;
controls.dampingFactor = 0.07;
controls.screenSpacePanning = true;
controls.minDistance = 3;
controls.maxDistance = 20;
controls.maxPolarAngle = Math.PI * 0.79;
controls.update();

const world = new THREE.Group();
const structureGroup = new THREE.Group();
const flagsGroup = new THREE.Group();
const centralGroup = new THREE.Group();
const ceilingGroup = new THREE.Group();
const ceilingLeftGroup = new THREE.Group();
const ceilingRightGroup = new THREE.Group();
const ceilingReserveGroup = new THREE.Group();
ceilingGroup.add(ceilingLeftGroup, ceilingRightGroup, ceilingReserveGroup);
ceilingReserveGroup.visible = false;
const outlineGroup = new THREE.Group();
const waveGroup = new THREE.Group();
const mediaGroup = new THREE.Group();
scene.add(world);
world.add(structureGroup, flagsGroup, centralGroup, ceilingGroup, outlineGroup, waveGroup, mediaGroup);
const editableObjects = [];
let sceneEditor;

const materials = {
  truss: new THREE.MeshStandardMaterial({ color: 0x36424f, metalness: 0.78, roughness: 0.34 }),
  trussDark: new THREE.MeshStandardMaterial({ color: 0x1c242e, metalness: 0.68, roughness: 0.45 }),
  stage: new THREE.MeshStandardMaterial({ color: 0x111720, metalness: 0.18, roughness: 0.78 }),
  fabric: new THREE.MeshStandardMaterial({ color: 0x07090d, metalness: 0.02, roughness: 0.94, side: THREE.DoubleSide }),
  booth: new THREE.MeshStandardMaterial({ color: 0x080b10, metalness: 0.2, roughness: 0.6 }),
  lineBase: new THREE.MeshBasicMaterial({ color: 0x2b8054, transparent: true, opacity: 0.88, toneMapped: false }),
  ceilingBase: new THREE.MeshBasicMaterial({ color: 0x173445, transparent: true, opacity: 0.48, toneMapped: false }),
  outlineBase: new THREE.MeshBasicMaterial({ color: 0x2f9664, transparent: true, opacity: 0.92, toneMapped: false }),
  waveBase: new THREE.MeshBasicMaterial({ color: 0x256c58, transparent: true, opacity: 0.90, toneMapped: false }),
  flagFrame: new THREE.MeshStandardMaterial({ color: 0x18232d, metalness: 0.64, roughness: 0.4 }),
  tvFrame: new THREE.MeshStandardMaterial({ color: 0x090c11, metalness: 0.72, roughness: 0.32 }),
  rack: new THREE.MeshStandardMaterial({ color: 0x252e38, metalness: 0.82, roughness: 0.28 })
};

const ndiCanvas = document.createElement('canvas');
ndiCanvas.width = 960;
ndiCanvas.height = 540;
const ndiContext = ndiCanvas.getContext('2d');
const ndiTexture = new THREE.CanvasTexture(ndiCanvas);
ndiTexture.colorSpace = THREE.SRGBColorSpace;
const ndiMaterial = new THREE.MeshBasicMaterial({ map: ndiTexture, toneMapped: false });

function drawNdiPlaceholder(message = 'NDI · SIN FUENTE') {
  const gradient = ndiContext.createLinearGradient(0, 0, ndiCanvas.width, ndiCanvas.height);
  gradient.addColorStop(0, '#071c1c');
  gradient.addColorStop(1, '#092a22');
  ndiContext.fillStyle = gradient;
  ndiContext.fillRect(0, 0, ndiCanvas.width, ndiCanvas.height);
  ndiContext.strokeStyle = 'rgba(91,255,188,.16)';
  ndiContext.lineWidth = 2;
  for (let x = 0; x < ndiCanvas.width; x += 48) {
    ndiContext.beginPath();
    ndiContext.moveTo(x, 0);
    ndiContext.lineTo(x, ndiCanvas.height);
    ndiContext.stroke();
  }
  ndiContext.fillStyle = '#b9ffe1';
  ndiContext.font = '500 46px Segoe UI, sans-serif';
  ndiContext.textAlign = 'center';
  ndiContext.textBaseline = 'middle';
  ndiContext.fillText(message, ndiCanvas.width / 2, ndiCanvas.height / 2);
  ndiTexture.needsUpdate = true;
}

drawNdiPlaceholder();

function addBox(name, size, position, material, parent = structureGroup, castShadow = true) {
  const mesh = new THREE.Mesh(new THREE.BoxGeometry(...size), material);
  mesh.name = name;
  mesh.position.set(...position);
  mesh.castShadow = castShadow;
  mesh.receiveShadow = true;
  parent.add(mesh);
  return mesh;
}

function cylinderBetween(start, end, radius, material, parent = structureGroup, radialSegments = 9) {
  const a = start instanceof THREE.Vector3 ? start : new THREE.Vector3(...start);
  const b = end instanceof THREE.Vector3 ? end : new THREE.Vector3(...end);
  const delta = new THREE.Vector3().subVectors(b, a);
  const length = delta.length();
  const mesh = new THREE.Mesh(new THREE.CylinderGeometry(radius, radius, length, radialSegments, 1), material);
  mesh.position.copy(a).add(b).multiplyScalar(0.5);
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), delta.normalize());
  mesh.castShadow = true;
  mesh.receiveShadow = true;
  parent.add(mesh);
  return mesh;
}

function addTubeBase(points, radius, material, parent) {
  const curve = new THREE.CatmullRomCurve3(points, false, 'centripetal');
  const mesh = new THREE.Mesh(new THREE.TubeGeometry(curve, Math.max(48, points.length), radius, 7, false), material);
  mesh.frustumCulled = false;
  parent.add(mesh);
  return mesh;
}

function buildEnvironment() {
  const floor = new THREE.Mesh(
    new THREE.PlaneGeometry(22, 16),
    new THREE.MeshStandardMaterial({ color: 0x07101a, metalness: 0.18, roughness: 0.88 })
  );
  floor.rotation.x = -Math.PI / 2;
  floor.position.y = 0;
  floor.receiveShadow = true;
  scene.add(floor);

  const grid = new THREE.GridHelper(20, 40, 0x1b4967, 0x102a40);
  grid.position.y = 0.004;
  grid.material.transparent = true;
  grid.material.opacity = 0.38;
  scene.add(grid);

  scene.add(new THREE.HemisphereLight(0x8dbbff, 0x071019, 0.72));
  const key = new THREE.DirectionalLight(0xd7e8ff, 1.4);
  key.position.set(-3.8, 6.5, 5.2);
  key.castShadow = true;
  key.shadow.mapSize.set(2048, 2048);
  scene.add(key);
  const green = new THREE.PointLight(0x51ff9b, 5.5, 7.5, 2);
  green.position.set(0, 1.1, 0.2);
  scene.add(green);
}

function buildStand() {
  addBox('Tarima 7 × 2 m', [7, 0.12, 2], [0, 0.06, 0], materials.stage);
  addBox('Canto frontal', [7.08, 0.20, 0.08], [0, 0.10, 1.02], materials.trussDark);
  const roofRise = 1.0;
  const roofHalfSpan = 3.5;
  const roofSlopeLength = Math.hypot(roofHalfSpan, roofRise);
  const roofAngle = Math.atan2(roofRise, roofHalfSpan);
  const leftRoof = addBox('Techo dos aguas izquierdo · 3.64 m', [roofSlopeLength, 0.07, 2.08], [-1.75, 4.50, 0], materials.fabric, structureGroup, false);
  leftRoof.rotation.z = roofAngle;
  const rightRoof = addBox('Techo dos aguas derecho · 3.64 m', [roofSlopeLength, 0.07, 2.08], [1.75, 4.50, 0], materials.fabric, structureGroup, false);
  rightRoof.rotation.z = -roofAngle;
  addBox('Fondo negro', [7, 3.80, 0.035], [0, 2.02, -1.00], materials.fabric, structureGroup, false);

  const corners = [
    [-3.5, 0, -1.00], [3.5, 0, -1.00], [-3.5, 0, 1.00], [3.5, 0, 1.00]
  ];
  corners.forEach(([x, y, z]) => cylinderBetween([x, 0.12, z], [x, 4.0, z], 0.034, materials.truss));
  for (const z of [-1.00, 1.00]) {
    cylinderBetween([-3.5, 4.0, z], [0, 5.0, z], 0.034, materials.truss);
    cylinderBetween([0, 5.0, z], [3.5, 4.0, z], 0.034, materials.truss);
  }
  for (const x of [-3.5, 3.5]) cylinderBetween([x, 4.0, -1.00], [x, 4.0, 1.00], 0.034, materials.truss);
  cylinderBetween([0, 5.0, -1.00], [0, 5.0, 1.00], 0.034, materials.truss);

  // The central desk and its logo were removed; retain the independent TV/rack.
}

function buildTvRack(x) {
  const firstChild = mediaGroup.children.length;
  const screenY = 1.62;
  const screenZ = -0.945; // Back wall Z=-1 m; wheels and cabinet stay inside the stand.
  addBox('TV 55 pulgadas', [1.30, 0.765, 0.065], [x, screenY, screenZ], materials.tvFrame, mediaGroup);
  const panel = new THREE.Mesh(new THREE.PlaneGeometry(1.218, 0.685), ndiMaterial);
  panel.position.set(x, screenY, screenZ + 0.036);
  panel.renderOrder = 12;
  mediaGroup.add(panel);
  cylinderBetween([x - 0.34, 0.25, screenZ], [x - 0.34, 1.22, screenZ], 0.018, materials.rack, mediaGroup);
  cylinderBetween([x + 0.34, 0.25, screenZ], [x + 0.34, 1.22, screenZ], 0.018, materials.rack, mediaGroup);
  cylinderBetween([x - 0.52, 0.24, screenZ], [x + 0.52, 0.24, screenZ], 0.022, materials.rack, mediaGroup);
  for (const wheelX of [x - 0.46, x + 0.46]) {
    const wheel = new THREE.Mesh(new THREE.SphereGeometry(0.055, 10, 7), materials.tvFrame);
    wheel.position.set(wheelX, 0.16, screenZ);
    mediaGroup.add(wheel);
  }
  editableObjects.push({ id: `TV-${x < 0 ? 'L' : 'R'}`, label: `TV 55″ ${x < 0 ? 'izquierda' : 'derecha'} + rack`, parent: mediaGroup, members: mediaGroup.children.slice(firstChild) });
}

function buildTvs() {
  buildTvRack(-2.75);
  buildTvRack(2.75);
}

function textMaterial(text, fontSize = 92) {
  const canvas = document.createElement('canvas');
  canvas.width = 1024;
  canvas.height = 256;
  const context = canvas.getContext('2d');
  context.clearRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = '#f7fbff';
  context.font = `500 ${fontSize}px Arial, sans-serif`;
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  context.fillText(text, canvas.width / 2, canvas.height / 2);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthWrite: false, toneMapped: false });
}

function addTextPlane(text, width, height, position, parent, fontSize = 92) {
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(width, height), textMaterial(text, fontSize));
  mesh.position.set(...position);
  parent.add(mesh);
  return mesh;
}

const flagBottomY = 0.10 + CONFIG.flag_width_m / 2;
const flagElements = ELEMENTS.filter(element => element.kind === 'flag');
const legacyFlagPlacementBySlot = Object.freeze({
  'upper-left-outer': { x: -2.75, y: 2.75, z: 0.13, yaw: -Math.PI / 4, tier: 'B2', portrait: true, side: 'izquierda' },
  'upper-left-inner': { x: -2.05, y: 3.45, z: 0.43, yaw: -Math.PI / 4, tier: 'B1', portrait: true, side: 'izquierda' },
  'upper-right-inner': { x: 2.05, y: 3.45, z: 0.43, yaw: Math.PI / 4, tier: 'B1', portrait: true, side: 'derecha' },
  'upper-right-outer': { x: 2.75, y: 2.75, z: 0.13, yaw: Math.PI / 4, tier: 'B2', portrait: true, side: 'derecha' },
  'lower-left': { x: -2.75, y: flagBottomY, z: -0.17, yaw: Math.PI / 4, tier: 'B3', portrait: true, side: 'izquierda' },
  'lower-right': { x: 2.75, y: flagBottomY, z: -0.17, yaw: -Math.PI / 4, tier: 'B3', portrait: true, side: 'derecha' }
});
function flagSlotFor(element) {
  return element.visual?.slot || element.visual_slot;
}
function flagPlacementFor(element) {
  const slot = flagSlotFor(element);
  const fallback = legacyFlagPlacementBySlot[slot];
  const portableSlot = element.visual?.placement || VISUAL_LAYOUT?.flags?.slots?.[slot];
  const base = portableSlot?.base_placement;
  const position = numericTriple(base?.position_m);
  if (!fallback || !position) return fallback;
  const yaw = Number(base.yaw_deg);
  return {
    ...fallback,
    x: position[0], y: position[1], z: position[2],
    yaw: Number.isFinite(yaw) ? THREE.MathUtils.degToRad(yaw) : fallback.yaw,
    tier: typeof base.tier === 'string' ? base.tier : fallback.tier,
    portrait: typeof base.portrait === 'boolean' ? base.portrait : fallback.portrait
  };
}
const flagPlacements = flagElements.map(flagPlacementFor);
const flagSlots = flagElements.map(flagSlotFor);
if (flagElements.length !== 6 || flagPlacements.some(placement => !placement) || new Set(flagSlots).size !== 6) {
  throw new Error('Patch de banderas inválido: se requieren seis posiciones visuales únicas.');
}
const flagInitialOverrides = Object.fromEntries(flagElements.map(element => {
  const slot = flagSlotFor(element);
  const portableSlot = element.visual?.placement || VISUAL_LAYOUT?.flags?.slots?.[slot];
  return [element.element_id, sceneTransform(portableSlot?.assembly_transform, FLAG_SLOT_TRANSFORMS[slot])];
}));

const waveElements = ELEMENTS.filter(element => element.kind === 'wave');
function waveVisualFor(element) {
  const groupId = element.visual?.group || element.element_id;
  const portableGroup = VISUAL_LAYOUT?.waves?.groups?.[groupId];
  const assembly = element.visual?.assembly_transform || portableGroup?.assembly_transform;
  const geometryTransform = element.visual?.geometry_transform || portableGroup?.geometry_transform;
  const legacy = CURRENT_LAYOUT[element.element_id] || {
    x: element.element_id === 'WAVE-LEFT' ? -BANNER.centerX : BANNER.centerX,
    y: 0, z: BANNER.z, rx: 0, ry: 0, rz: 0
  };
  return {
    groupId,
    position: numericTriple(assembly?.position_m) || [legacy.x, legacy.y, legacy.z],
    rotation: numericTriple(assembly?.rotation_deg_xyz) || [legacy.rx || 0, legacy.ry || 0, legacy.rz || 0],
    scale: numericTriple(assembly?.scale_xyz) || [1, 1, 1],
    mirrorAxis: geometryTransform?.mirror_axis ?? (element.element_id === 'WAVE-RIGHT' ? 'X' : null),
    assembly
  };
}
const waveInitialOverrides = Object.fromEntries(waveElements.map(element => {
  const visual = waveVisualFor(element);
  return [element.element_id, sceneTransform(visual.assembly, CURRENT_LAYOUT[element.element_id])];
}));
function wavePathsFor(element) {
  const visual = waveVisualFor(element);
  const bannerSize = numericTriple([...(VISUAL_LAYOUT?.waves?.geometry?.banner_size_m || [BANNER.width, BANNER.height]), 0].slice(0, 3));
  const width = bannerSize?.[0] || BANNER.width;
  const mirrorX = visual.mirrorAxis === 'X' ? -1 : 1;
  return BANNER_PATHS.map(path => path.map(([x, y]) => new THREE.Vector3(
    visual.position[0] + (x - width / 2) * visual.scale[0] * mirrorX,
    visual.position[1] + y * visual.scale[1],
    visual.position[2]
  )));
}

function transformFlagPoint(localX, localY, placement) {
  // Rotate the same 60x42 fixture in its own plane, then apply yaw. No pixel reorder.
  if (placement.portrait) [localX, localY] = [-localY, localX];
  const cos = Math.cos(placement.yaw);
  const sin = Math.sin(placement.yaw);
  return new THREE.Vector3(
    placement.x + localX * cos,
    placement.y + localY,
    placement.z - localX * sin
  );
}

function flagPoints(flagIndex) {
  const placement = flagPlacements[flagIndex];
  const points = [];
  for (let row = 0; row < CONFIG.flag_height; row++) {
    for (let column = 0; column < CONFIG.flag_width; column++) {
      const x = (column - (CONFIG.flag_width - 1) / 2) * CONFIG.flag_pitch_m;
      const y = ((CONFIG.flag_height - 1) / 2 - row) * CONFIG.flag_pitch_m;
      points.push(transformFlagPoint(x, y, placement));
    }
  }
  return points;
}

function buildFlagFabric() {
  flagPlacements.forEach((placement, flagIndex) => {
    const width = placement.portrait ? CONFIG.flag_height_m : CONFIG.flag_width_m;
    const height = placement.portrait ? CONFIG.flag_width_m : CONFIG.flag_height_m;
    const group = new THREE.Group();
    group.position.set(placement.x, placement.y, placement.z - 0.012);
    group.rotation.y = placement.yaw;
    const backing = new THREE.Mesh(
      new THREE.PlaneGeometry(width, height),
      new THREE.MeshBasicMaterial({
        color: 0x06100c,
        transparent: true,
        opacity: 0.34,
        side: THREE.DoubleSide,
        depthWrite: false
      })
    );
    group.add(backing);
    const halfW = width / 2 + 0.025;
    const halfH = height / 2 + 0.025;
    cylinderBetween([-halfW, -halfH, 0], [halfW, -halfH, 0], 0.018, materials.flagFrame, group);
    cylinderBetween([-halfW, halfH, 0], [halfW, halfH, 0], 0.018, materials.flagFrame, group);
    cylinderBetween([-halfW, -halfH, 0], [-halfW, halfH, 0], 0.018, materials.flagFrame, group);
    cylinderBetween([halfW, -halfH, 0], [halfW, halfH, 0], 0.018, materials.flagFrame, group);
    if (placement.tier === 'B1') {
      for (const localX of [-width / 2, width / 2]) {
        const worldX = placement.x + localX * Math.cos(placement.yaw);
        const roofY = 5 - Math.abs(worldX) / 3.5;
        cylinderBetween([localX, halfH, 0], [localX, roofY - placement.y, 0], 0.003, materials.flagFrame, group);
      }
    }
    if (placement.tier === 'B3') {
      for (const localX of [-width * 0.38, width * 0.38]) {
        cylinderBetween([localX, 0.095 - placement.y, -0.20], [localX, 0.095 - placement.y, 0.20], 0.012, materials.rack, group);
        for (const z of [-0.17, 0.17]) {
          const wheel = new THREE.Mesh(new THREE.CylinderGeometry(0.045, 0.045, 0.035, 12), materials.tvFrame);
          wheel.name = 'Rueda de bandera B3';
          wheel.rotation.z = Math.PI / 2;
          wheel.position.set(localX, 0.045 - placement.y, z);
          group.add(wheel);
        }
      }
    }
    group.name = `${flagElements[flagIndex].label} · ${placement.tier} · ${placement.portrait ? 'vertical 1.05 × 1.50' : 'horizontal'}`;
    flagsGroup.add(group);
  });
}

function centralLinePoints(index, count) {
  const named = ELEMENTS.filter(e => e.kind === 'central')[index];
  if (named.tree_bottom && named.tree_top) {
    const a=new THREE.Vector3(...named.tree_bottom),b=new THREE.Vector3(...named.tree_top);
    return Array.from({length:count},(_,i)=>new THREE.Vector3().lerpVectors(a,b,(i+.5)/count));
  }
  const familySize = CONFIG.central_strands / 2;
  const family = Math.floor(index / familySize);
  const slot = index % familySize;
  const phase = slot * Math.PI * 2 / familySize + family * Math.PI / familySize;
  const direction = family === 0 ? 1 : -1;
  const waistRadius = CONFIG.central_waist_radius_m;
  const halfProjection = CONFIG.central_horizontal_projection_m / 2;
  const midpoint = new THREE.Vector3(
    Math.cos(phase) * waistRadius,
    CONFIG.central_vertical_height_m / 2,
    Math.sin(phase) * waistRadius
  );
  const tangent = new THREE.Vector3(-Math.sin(phase), 0, Math.cos(phase)).multiplyScalar(direction * halfProjection);
  const bottom = midpoint.clone().sub(tangent);
  bottom.y = 0;
  const top = midpoint.clone().add(tangent);
  top.y = CONFIG.central_vertical_height_m;
  const points = [];
  for (let pixel = 0; pixel < count; pixel++) {
    points.push(new THREE.Vector3().lerpVectors(bottom, top, pixel / Math.max(1, count - 1)));
  }
  return points;
}

function ceilingLinePoints(index, count) {
  return ceilingPath(index).map(p => new THREE.Vector3(...p));
}
function polylineLength(points) {
  let length = 0;
  for (let index = 1; index < points.length; index++) length += points[index - 1].distanceTo(points[index]);
  return length;
}

function resamplePolyline(source, count) {
  const cumulative = [0];
  for (let index = 1; index < source.length; index++) {
    cumulative.push(cumulative[index - 1] + source[index - 1].distanceTo(source[index]));
  }
  const total = cumulative[cumulative.length - 1];
  const points = [];
  let cursor = 1;
  for (let pixel = 0; pixel < count; pixel++) {
    const target = total * pixel / Math.max(1, count - 1);
    while (cursor < cumulative.length - 1 && cumulative[cursor] < target) cursor++;
    const previous = cursor - 1;
    const span = Math.max(1e-9, cumulative[cursor] - cumulative[previous]);
    points.push(new THREE.Vector3().lerpVectors(source[previous], source[cursor], (target - cumulative[previous]) / span));
  }
  return points;
}

function outlineLinePoints(element, count) {
  const z = 1.085;
  const segments = {
    'HOUSE-LEFT-POST': [[-3.5, 0, z], [-3.5, 4.0, z]],
    'HOUSE-LEFT-ROOF': [[-3.5, 4.0, z], [0, 5.0, z]],
    'HOUSE-RIGHT-ROOF': [[0, 5.0, z], [3.5, 4.0, z]],
    'HOUSE-RIGHT-POST': [[3.5, 4.0, z], [3.5, 0, z]],
    'OUTLINE-LEFT-POST': [[-3.5, 0, z], [-3.5, 4.0, z]],
    'OUTLINE-LEFT-ROOF': [[-3.5, 4.0, z], [0, 5.0, z]],
    'OUTLINE-RIGHT-ROOF': [[0, 5.0, z], [3.5, 4.0, z]],
    'OUTLINE-RIGHT-POST': [[3.5, 4.0, z], [3.5, 0, z]],
    'OUTLINE-BASE': [[-3.5, 0.14, z], [3.5, 0.14, z]]
  };
  const segment = segments[element.element_id];
  if (!segment) throw new Error(`No existe geometría para ${element.element_id}.`);
  const start = new THREE.Vector3(...segment[0]);
  const end = new THREE.Vector3(...segment[1]);
  const points = [];
  for (let pixel = 0; pixel < count; pixel++) {
    points.push(new THREE.Vector3().lerpVectors(start, end, pixel / Math.max(1, count - 1)));
  }
  return points;
}

function waveLinePoints(element, count) {
  const side = element.element_id === 'WAVE-LEFT' ? -1 : 1;
  return WAVE_PATH.map(([x, y]) => new THREE.Vector3(side * BANNER.centerX + x, y, BANNER.z));
}

function buildBannerBackings() {
  const geometry = VISUAL_LAYOUT?.waves?.geometry;
  const bannerSize = geometry?.banner_size_m || [BANNER.width, BANNER.height];
  const width = Number(bannerSize[0]) || BANNER.width;
  const height = Number(bannerSize[1]) || BANNER.height;
  const pathWidth = Number(geometry?.path_width_m) || BANNER.pathWidth;
  const postCount = Number(geometry?.post_count) || 5;
  for (const element of waveElements) {
    const visual = waveVisualFor(element);
    const backingGroup = new THREE.Group();
    backingGroup.position.set(...visual.position);
    backingGroup.scale.set(...visual.scale);
    waveGroup.add(backingGroup);
    const banner = new THREE.Mesh(new THREE.PlaneGeometry(width, height),
      new THREE.MeshBasicMaterial({ color: 0x040707, side: THREE.DoubleSide }));
    banner.name = `Banner de ondas ${visual.position[0] < 0 ? 'izquierdo' : 'derecho'} · ${width} × ${height} m`;
    banner.position.set(0, height / 2, -0.03);
    backingGroup.add(banner);
    const mirrorX = visual.mirrorAxis === 'X' ? -1 : 1;
    const postXs = Array.from({ length: postCount }, (_, i) =>
      mirrorX * (-width / 2 + .0327 + i * pathWidth / Math.max(1, postCount - 1)));
    for (const [i, x] of postXs.entries()) {
      const post = cylinderBetween([x, 0, -.045], [x, height, -.045], .014, materials.rack, backingGroup);
      post.name = `Parante ${i + 1} · ${Math.round(height * 100)} cm`;
    }
    for (const y of [.0327, height - .0327]) {
      cylinderBetween([mirrorX * -width / 2, y, -.052], [mirrorX * width / 2, y, -.052], .014, materials.rack, backingGroup);
    }
    editableObjects.find(object => object.id === element.element_id)?.members.push(backingGroup);
  }
}

const pointRecords = [];
const pickablePointMeshes = [];
let globalPixelOffset = 0;

function addMappedPoints(element, points, parent, size) {
  if (element.kind !== 'flag') {
    const neon = element.kind === 'wave'
      ? createNeonRuns(wavePathsFor(element), Number(VISUAL_LAYOUT?.waves?.geometry?.pixels_per_row || BANNER.pixels))
      : createNeonPixels(points, element.pixel_count);
    neon.colors.fill(0.005);
    neon.mesh.name = element.label;
    neon.mesh.userData.recordIndex = pointRecords.length;
    parent.add(neon.mesh);
    pointRecords.push({ element, ...neon, globalOffset: globalPixelOffset });
    pickablePointMeshes.push(neon.mesh);
    globalPixelOffset += element.pixel_count;
    return;
  }
  if (points.length !== element.pixel_count) {
    throw new Error(`${element.element_id}: la geometría tiene ${points.length} px y el patch ${element.pixel_count} px.`);
  }
  const positions = new Float32Array(points.length * 3);
  const colors = new Float32Array(points.length * 3);
  points.forEach((point, index) => {
    positions[index * 3] = point.x;
    positions[index * 3 + 1] = point.y;
    positions[index * 3 + 2] = point.z;
    colors[index * 3] = 0.003;
    colors[index * 3 + 1] = 0.006;
    colors[index * 3 + 2] = 0.004;
  });
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  const material = new THREE.PointsMaterial({
    size,
    vertexColors: true,
    transparent: true,
    opacity: 1,
    sizeAttenuation: true,
    blending: THREE.AdditiveBlending,
    depthTest: false,
    depthWrite: false,
    toneMapped: false
  });
  const mesh = new THREE.Points(geometry, material);
  mesh.name = element.label;
  mesh.userData.recordIndex = pointRecords.length;
  mesh.frustumCulled = false;
  mesh.renderOrder = 50;
  parent.add(mesh);
  pointRecords.push({ element, points, geometry, colors, mesh, globalOffset: globalPixelOffset });
  pickablePointMeshes.push(mesh);
  globalPixelOffset += element.pixel_count;
}

function ceilingGroupIdFor(element, placement) {
  if (element.visual?.group) return element.visual.group;
  if (!placement.active) return 'CEILING-RESERVE';
  return placement.side < 0 ? 'CEILING-LEFT' : 'CEILING-RIGHT';
}

function buildMappedElements() {
  let flagIndex = 0;
  let centralIndex = 0;
  let ceilingIndex = 0;
  for (const element of ELEMENTS) {
    const physicalCeilingIndex = element.kind === 'ceiling' ? Number(element.layout_index ?? ceilingIndex) : ceilingIndex;
    const placement = ceilingPlacement(physicalCeilingIndex);
    const ceilingGroupId = element.kind === 'ceiling' ? ceilingGroupIdFor(element, placement) : null;
    const ceilingSide = ({
      'CEILING-LEFT': ceilingLeftGroup,
      'CEILING-RIGHT': ceilingRightGroup,
      'CEILING-RESERVE': ceilingReserveGroup
    })[ceilingGroupId] || ceilingReserveGroup;
    const parent = ({ flag: flagsGroup, central: centralGroup, ceiling: ceilingSide, outline: outlineGroup, wave: waveGroup })[element.kind];
    const firstChild = parent?.children.length;
    const fabric = element.kind === 'flag' ? flagsGroup.children[flagIndex] : null;
    if (element.kind === 'flag') {
      addMappedPoints(element, flagPoints(flagIndex++), flagsGroup, 0.024);
    } else if (element.kind === 'central') {
      addMappedPoints(element, centralLinePoints(centralIndex++, element.pixel_count), centralGroup, 0.070);
    } else if (element.kind === 'ceiling') {
      addMappedPoints(element, ceilingLinePoints(physicalCeilingIndex, element.pixel_count), parent, 0.039);
      if (placement.active) for (const [supportIndex, anchor] of ceilingAnchors(physicalCeilingIndex).entries()) {
        const fitting = new THREE.Mesh(new THREE.SphereGeometry(0.022, 8, 6), materials.rack);
        fitting.position.set(...anchor); fitting.name = `${CEILING_SUPPORTS[supportIndex]} · anclaje ${anchor[1].toFixed(2)} m`;
        fitting.userData.supportId = CEILING_SUPPORTS[supportIndex];
        parent.add(fitting);
      }
      ceilingIndex++;
    } else if (element.kind === 'outline') {
      addMappedPoints(element, outlineLinePoints(element, element.pixel_count), outlineGroup, 0.070);
    } else if (element.kind === 'wave') {
      addMappedPoints(element, waveLinePoints(element, element.pixel_count), waveGroup, 0.070);
    }
    if (parent) editableObjects.push({ id: element.element_id, label: element.kind === 'flag' ? `${flagPlacements[flagIndex - 1].tier} · ${element.label}` : element.label, parent,
      selectionGroup: element.kind === 'central' ? 'CENTRAL-TREE' : element.kind === 'ceiling' ? ceilingGroupId : null,
      defaultCount: element.kind === 'ceiling' && !placement.active ? 0 : 1,
      pivot: element.kind === 'flag' ? new THREE.Vector3(flagPlacements[flagIndex - 1].x, flagPlacements[flagIndex - 1].y, flagPlacements[flagIndex - 1].z) : element.kind === 'wave' ? new THREE.Vector3(...waveVisualFor(element).position) : null,
      members: [...(fabric ? [fabric] : []), ...parent.children.slice(firstChild)], record: pointRecords.at(-1) });
  }
  if (globalPixelOffset !== CONFIG.visible_pixels) {
    throw new Error(`Patch incompleto: ${globalPixelOffset} de ${CONFIG.visible_pixels} píxeles.`);
  }
}

function clamp01(value) {
  return Math.min(1, Math.max(0, value));
}

function mixEffectColor(a, b, amount, intensity) {
  return new THREE.Color().copy(a).lerp(b, clamp01(amount)).multiplyScalar(Math.max(0, intensity));
}

function effectColor(record, localIndex, position, time) {
  const normalized = localIndex / Math.max(1, record.element.pixel_count - 1);
  const x = (position.x + 3.5) / 7;
  const y = position.y / 5;
  if (state.effect === 'solid') return state.colorA.clone();
  if (state.effect === 'pulse') {
    const pulse = 0.22 + 0.78 * Math.pow((Math.sin(time * 2.5) + 1) / 2, 3);
    return mixEffectColor(state.colorA, state.colorB, 0.32, pulse);
  }
  if (state.effect === 'horizontal') {
    const wave = Math.exp(-Math.pow((((x - time * 0.22) % 1 + 1) % 1) - 0.5, 2) * 80);
    return mixEffectColor(state.colorA, state.colorB, x, 0.07 + wave * 1.25);
  }
  if (state.effect === 'vertical') {
    const wave = Math.exp(-Math.pow((((y - time * 0.28) % 1 + 1) % 1) - 0.5, 2) * 90);
    return mixEffectColor(state.colorB, state.colorA, y, 0.06 + wave * 1.18);
  }
  if (state.effect === 'rainbow') {
    return new THREE.Color().setHSL((x * 0.42 + y * 0.36 + position.z * 0.08 - time * 0.08 + 1) % 1, 0.92, 0.56);
  }
  if (state.effect === 'address') {
    const phase = (record.globalOffset + localIndex) / CONFIG.visible_pixels;
    const active = Math.abs((((phase - time * 0.05) % 1 + 1.5) % 1) - 0.5) < 0.025;
    return mixEffectColor(state.colorA, state.colorB, phase, active ? 1.25 : 0.035);
  }
  const braid = Math.sin(normalized * Math.PI * 8 - time * 2.4 + record.globalOffset * 0.011);
  const lift = Math.sin(y * Math.PI * 7 - time * 2.1);
  const intensity = 0.08 + Math.pow(Math.max(0, braid * 0.55 + lift * 0.45), 5) * 1.35;
  return mixEffectColor(state.colorA, state.colorB, (braid + 1) / 2, intensity);
}

const renderColor = new THREE.Color();

function artNetIsLive() {
  if (artNetState.livePreview) return artNetState.outputSignal && !artNetState.receiverError && performance.now() - artNetState.liveReceivedAt < 2000;
  return artNetState.lastPacketMs >= 0 && artNetState.lastPacketMs < 2000 && !artNetState.receiverError;
}

function updatePointColors() {
  const artNetLive = state.mode === 'artnet' && artNetIsLive() && artNetState.colors;
  for (const record of pointRecords) {
    for (let localIndex = 0; localIndex < record.element.pixel_count; localIndex++) {
      const colorIndex = localIndex * 3;
      if (state.blackout) {
        renderColor.setRGB(0, 0, 0);
      } else if (artNetLive) {
        const portableMapping = record.element.visual?.pixel_mapping;
        const route = artNetState.routeColors[portableMapping?.route_id || record.element.route_id];
        const routePixel = Number(portableMapping?.route_offset_pixels ?? record.element.route_offset ?? 0) + sourcePixelFor(record.element, localIndex);
        const globalIndex = routePixel * 3;
        renderColor.setRGB(
          (route?.[globalIndex] || 0) / 255,
          (route?.[globalIndex + 1] || 0) / 255,
          (route?.[globalIndex + 2] || 0) / 255
        );
      } else if (state.mode === 'simulation') {
        renderColor.copy(effectColor(record, localIndex, record.points[localIndex], state.elapsed));
      } else {
        renderColor.setRGB(0, 0, 0);
      }
      renderColor.multiplyScalar(state.brightness);
      record.colors[colorIndex] = renderColor.r;
      record.colors[colorIndex + 1] = renderColor.g;
      record.colors[colorIndex + 2] = renderColor.b;
    }
    if (record.mesh.isInstancedMesh) record.mesh.instanceColor.needsUpdate = true;
    else record.geometry.attributes.color.needsUpdate = true;
  }
}

function decodeBase64(value) {
  const binary = atob(value || '');
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index++) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

function handleHostMessage(payload) {
  let message = payload;
  if (typeof message === 'string') {
    try { message = JSON.parse(message); } catch { return; }
  }
  if (!message || typeof message !== 'object') return;
  if (message.type === 'outputFrame') {
    artNetState.livePreview = true;
    artNetState.liveReceivedAt = performance.now();
    artNetState.outputSignal = Boolean(message.signal);
    artNetState.routeColors = Object.fromEntries(Object.entries(message.routes || {}).map(([id,value])=>[id,decodeBase64(value)]));
    artNetState.colors = true;
    artNetState.packetRate = Number(message.packetRate || 0);
    artNetState.sources = message.sender ? [message.sender] : [];
    artNetState.receiverError = message.error || null;
    artNetState.input = message.source || 'resolume';
    ui.modeLabel.textContent = message.error ? `Error · ${message.error}` : message.generalTest ? 'Test general · salida real al 25%' :
      message.signal ? `${message.sourceLabel || 'Fuente'} · salida en vivo` : `${message.sourceLabel || 'Fuente'} · esperando señal`;
    ui.modeDot.classList.toggle('active', Boolean(message.signal) && !message.error);
    updatePointColors();
    return;
  }
  if (message.type === 'liveMode') {
    ui.mode.value = 'artnet'; updateMode();
  }
  if (message.type === 'artnetFrame') {
    artNetState.livePreview = Boolean(message.livePreview);
    artNetState.liveReceivedAt = performance.now();
    if (artNetState.pendingInput && message.input && message.input !== artNetState.pendingInput) return;
    if (message.input) {
      artNetState.pendingInput = null;
      artNetState.input = message.input;
      ui.artNetInput.value = message.input;
      artNetState.inputFilters = message.inputFilters || artNetState.inputFilters;
      if (document.activeElement !== ui.artNetInputIp) ui.artNetInputIp.value = artNetState.inputFilters[message.input] || '';
      const senders = Array.isArray(message.availableSources) ? message.availableSources : [];
      ui.artNetSenders.replaceChildren(...senders.map(ip => { const o=document.createElement('option');o.value=ip;return o; }));
      const addresses = (message.receiverAddresses || []).filter(ip => !ip.startsWith('127.'));
      ui.receiverAddresses.textContent = addresses.length ? `IP de esta PC para Unity: ${addresses.join(' / ')} · UDP 6454` : 'Sin IP LAN detectada. Conecta esta PC a la red de Unity.';
      ui.artNetInputStatus.textContent = `${message.input === 'unity' ? 'B · Unity/red' : 'A · Local/puente'} · ${message.sources?.[0] || 'esperando emisor'} · sin mezcla. Señal ausente >2 s: negro.`;
    }
    artNetState.colors = decodeBase64(message.colors);
    artNetState.packets = Number(message.packets || 0);
    artNetState.packetRate = Number(message.packetRate || 0);
    artNetState.universes = Number(message.universes || 0);
    artNetState.expectedUniverses = Number(message.expectedUniverses || CONFIG.used_universes);
    artNetState.lastPacketMs = Number(message.lastPacketMs ?? -1);
    artNetState.sources = Array.isArray(message.sources) ? message.sources : [];
    artNetState.receiverError = message.receiverError || null;
    updateArtNetUi();
    if (state.mode === 'artnet') updatePointColors();
  }
  if (message.type === 'artnetInputError') {
    artNetState.pendingInput = null;
    showNotice(message.message || 'No se pudo cambiar la entrada.');
  }
  if (message.type === 'resolumeInstallResult') {
    ui.installStatus.textContent = message.message || (message.success ? 'Instalado.' : 'No se pudo instalar.');
    ui.installStatus.classList.toggle('success', Boolean(message.success));
    ui.installStatus.classList.toggle('error', !message.success);
  }
  if (message.type === 'ndiStatus') {
    const selected = message.selectedSource || ui.ndiSource.value;
    const sources = Array.isArray(message.sources) ? message.sources : [];
    ui.ndiSource.innerHTML = '<option value="">Sin fuente NDI</option>';
    for (const source of sources) {
      const option = document.createElement('option');
      option.value = source;
      option.textContent = source;
      option.selected = source === selected;
      ui.ndiSource.appendChild(option);
    }
    if (message.error) ui.ndiStatus.textContent = message.error;
    else if (message.connected) ui.ndiStatus.textContent = `${message.width || 0} × ${message.height || 0} · ${Number(message.fps || 0).toFixed(1)} fps · 1 televisor`;
    else ui.ndiStatus.textContent = sources.length ? 'Selecciona una fuente para el televisor.' : 'Buscando fuentes NDI…';
  }
  if (message.type === 'ndiFrame' && message.image) {
    const image = new Image();
    image.onload = () => {
      ndiContext.fillStyle = '#000';
      ndiContext.fillRect(0, 0, ndiCanvas.width, ndiCanvas.height);
      const scale = Math.min(ndiCanvas.width / image.width, ndiCanvas.height / image.height);
      const width = image.width * scale;
      const height = image.height * scale;
      ndiContext.drawImage(image, (ndiCanvas.width - width) / 2, (ndiCanvas.height - height) / 2, width, height);
      ndiTexture.needsUpdate = true;
    };
    image.src = `data:image/jpeg;base64,${message.image}`;
  }
}

function hostPost(payload) {
  if (window.chrome?.webview) window.chrome.webview.postMessage(payload);
}

function applyArtNetInput() {
  const input = ui.artNetInput.value;
  const sourceIp = ui.artNetInputIp.value.trim();
  if (sourceIp && !/^(?:\d{1,3}\.){3}\d{1,3}$/.test(sourceIp)) { showNotice('Introduce una IPv4 de origen o deja Auto.'); return; }
  if (sourceIp && sourceIp.split('.').some(n => Number(n) > 255)) { showNotice('IP de origen no válida.'); return; }
  artNetState.inputFilters[input] = sourceIp;
  artNetState.input = input;
  artNetState.pendingInput = input;
  artNetState.colors = null;
  artNetState.packets = 0; artNetState.packetRate = 0; artNetState.universes = 0;
  artNetState.lastPacketMs = -1; artNetState.sources = [];
  updateArtNetUi(); updatePointColors();
  hostPost({ type: 'artnetInput', value: input, sourceIp });
  ui.artNetInputStatus.textContent = window.chrome?.webview ? 'Aplicando entrada…' : 'Selección de prueba: la recepción UDP funciona en el EXE, no en esta vista web.';
}

function updateArtNetUi() {
  const live = artNetIsLive();
  if (artNetState.receiverError) {
    ui.artNetSignal.textContent = 'Error del receptor';
    ui.artNetSource.textContent = artNetState.receiverError;
  } else if (live) {
    ui.artNetSignal.textContent = `${artNetState.universes}/${artNetState.expectedUniverses} universos · ${Math.round(artNetState.packetRate)} pkt/s`;
    ui.artNetSource.textContent = artNetState.sources.join(', ') || 'UDP 6454';
  } else {
    ui.artNetSignal.textContent = artNetState.packets ? 'Señal detenida' : 'Esperando paquetes';
    ui.artNetSource.textContent = artNetState.sources.join(', ') || '-';
  }

  if (state.mode === 'artnet') {
    ui.networkBadge.textContent = live ? 'EN VIVO' : 'SIN SEÑAL';
    ui.networkBadge.classList.toggle('ok', live);
    ui.modeDot.classList.toggle('active', live);
    ui.modeLabel.textContent = live ? `Art-Net en vivo · A1/B1 controlador · U0–${CONFIG.used_universes-1}` : 'Art-Net en vivo · esperando señal';
  }
}

function updateMode() {
  state.mode = ui.mode.value;
  const liveMode = state.mode === 'artnet';
  ui.effect.disabled = liveMode;
  ui.play.disabled = liveMode;
  ui.speed.disabled = liveMode;
  ui.modeHelp.textContent = liveMode
    ? `V19 slices / V20 A1-B1: UDP 6454 · U0–${CONFIG.used_universes-1} · RGB 170 px/U. Local/puente o Unity/red; IP de origen opcional.`
    : 'Los efectos se generan localmente para revisar la forma, el orden y el snake.';
  if (!liveMode) {
    ui.networkBadge.textContent = 'OFFLINE';
    ui.networkBadge.classList.add('ok');
    ui.modeDot.classList.add('active');
    ui.modeLabel.textContent = 'Simulación local · nuevo diseño';
  } else {
    updateArtNetUi();
  }
  hostPost({ type: 'mode', value: state.mode });
  updatePointColors();
}

function exportPatchCsv() {
  showNotice('Este CSV conserva el patch original. Los objetos nuevos y las copias necesitan un patch independiente.');
  const rows = [[
    'Elemento', 'Tipo', 'Descripcion', 'IP_fija', 'Salida', 'Pixeles',
    'Universo_inicio', 'Canal_inicio', 'Universo_fin', 'Canal_fin', 'Direccion'
  ]];
  ELEMENTS.filter(element => element.active).forEach(element => rows.push([
    element.element_id,
    element.kind,
    element.label,
    element.target_ip,
    element.output || 'BANDERA',
    element.pixel_count,
    element.universe_start,
    element.channel_start,
    element.universe_end,
    element.channel_end,
    element.direction
  ]));
  const csv = rows.map(row => row.map(value => `"${String(value).replaceAll('"', '""')}"`).join(',')).join('\r\n');
  const blob = new Blob(['\ufeff', csv], { type: 'text/csv;charset=utf-8' });
  const link = document.createElement('a');
  link.href = URL.createObjectURL(blob);
  link.download = 'ETH01-Stand-7x2-Patch.csv';
  link.click();
  URL.revokeObjectURL(link.href);
}

function showNotice(message) {
  ui.notice.textContent = message;
  ui.notice.classList.add('visible');
  clearTimeout(showNotice.timer);
  showNotice.timer = setTimeout(() => ui.notice.classList.remove('visible'), 3200);
}

function pixelAddress(element, visiblePixel) {
  if (!element.active) return { universe: -1, channel: 0 };
  const absolutePixel = element.pixel_start + sourcePixelFor(element, visiblePixel);
  return {
    universe: element.base_universe + Math.floor(absolutePixel / PIXELS_PER_UNIVERSE),
    channel: (absolutePixel % PIXELS_PER_UNIVERSE) * CHANNELS_PER_PIXEL + 1
  };
}

function updateLayerVisibility() {
  flagsGroup.visible = ui.showFlags.checked;
  // Visibility is applied to the complete tree and its copies by the editor.
  centralGroup.visible = true;
  ceilingGroup.visible = ui.showCeiling.checked;
  outlineGroup.visible = ui.showOutline.checked;
  waveGroup.visible = ui.showOutline.checked;
  mediaGroup.visible = ui.showTvs.checked;
  structureGroup.visible = ui.showStructure.checked;
}

const cameraPresets = {
  front: { position: [0, 1.75, 8.6], target: [0, 1.48, -0.10] },
  rear: { position: [0, 2.6, -8.6], target: [0, 2.4, -0.208] },
  perspective: { position: [6.1, 3.6, 6.4], target: [0, 1.45, -0.08] },
  side: { position: [7.4, 2.35, 0.7], target: [0, 1.45, -0.15] },
  top: { position: [0.2, 8.8, 0.4], target: [0, 1.25, 0] }
};

let cameraTween = null;

function setCamera(name) {
  const preset = cameraPresets[name];
  if (!preset) return;
  cameraTween = {
    started: performance.now(),
    duration: 620,
    fromPosition: camera.position.clone(),
    fromTarget: controls.target.clone(),
    toPosition: new THREE.Vector3(...preset.position),
    toTarget: new THREE.Vector3(...preset.target)
  };
  document.querySelectorAll('[data-camera]').forEach(button => button.classList.toggle('selected', button.dataset.camera === name));
}

function updateCameraTween(now) {
  if (!cameraTween) return;
  const progress = clamp01((now - cameraTween.started) / cameraTween.duration);
  const eased = 1 - Math.pow(1 - progress, 3);
  camera.position.lerpVectors(cameraTween.fromPosition, cameraTween.toPosition, eased);
  controls.target.lerpVectors(cameraTween.fromTarget, cameraTween.toTarget, eased);
  if (progress >= 1) cameraTween = null;
}

const raycaster = new THREE.Raycaster();
raycaster.params.Points.threshold = 0.055;
const pointer = new THREE.Vector2();

function pickPixel(event) {
  const rect = renderer.domElement.getBoundingClientRect();
  pointer.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
  pointer.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(pointer, camera);
  const hit = raycaster.intersectObjects(pickablePointMeshes, false)[0];
  const pixelIndex = hit?.instanceId ?? hit?.index;
  if (!hit || pixelIndex === undefined) {
    ui.selection.textContent = 'Haz clic en un píxel';
    return;
  }
  const record = pointRecords[hit.object.userData.recordIndex];
  sceneEditor?.select(record.element.element_id);
  const address = pixelAddress(record.element, pixelIndex);
  ui.selection.textContent = `${record.element.element_id} · px ${pixelIndex + 1}/${record.element.pixel_count} · ${record.element.target_ip} · U${address.universe} C${address.channel}`;
}

function bindUi() {
  ui.mode.addEventListener('change', updateMode);
  ui.artNetInput.addEventListener('change', () => {
    ui.artNetInputIp.value = artNetState.inputFilters[ui.artNetInput.value] || '';
    applyArtNetInput();
  });
  ui.artNetInputApply.addEventListener('click', applyArtNetInput);
  ui.artNetInputIp.addEventListener('keydown', event => { if(event.key === 'Enter') applyArtNetInput(); });
  ui.effect.addEventListener('change', () => { state.effect = ui.effect.value; updatePointColors(); });
  ui.play.addEventListener('click', () => {
    state.playing = !state.playing;
    ui.play.textContent = state.playing ? 'Pausar' : 'Reproducir';
    ui.playState.textContent = state.playing ? 'ACTIVO' : 'PAUSA';
    ui.playState.classList.toggle('ok', state.playing);
  });
  ui.blackout.addEventListener('click', () => {
    state.blackout = !state.blackout;
    ui.blackout.classList.toggle('selected', state.blackout);
    ui.blackout.textContent = state.blackout ? 'Restaurar' : 'Blackout';
    updatePointColors();
  });
  ui.brightness.addEventListener('input', () => {
    state.brightness = Number(ui.brightness.value) / 100;
    ui.brightnessValue.value = `${ui.brightness.value}%`;
    updatePointColors();
  });
  ui.speed.addEventListener('input', () => {
    state.speed = Number(ui.speed.value) / 100;
    ui.speedValue.value = `${state.speed.toFixed(2)}×`;
  });
  ui.colorA.addEventListener('input', () => { state.colorA.set(ui.colorA.value); updatePointColors(); });
  ui.colorB.addEventListener('input', () => { state.colorB.set(ui.colorB.value); updatePointColors(); });
  [ui.showFlags, ui.showCentral, ui.showCeiling, ui.showOutline, ui.showTvs, ui.showStructure].forEach(control => control.addEventListener('change', updateLayerVisibility));
  ui.ndiSource.addEventListener('change', () => {
    hostPost({ type: 'ndiSelect', value: ui.ndiSource.value });
    if (!ui.ndiSource.value) drawNdiPlaceholder();
  });
  ui.ndiRefresh.addEventListener('click', () => {
    hostPost({ type: 'ndiRefresh' });
    ui.ndiStatus.textContent = 'Actualizando fuentes NDI…';
  });
  document.querySelectorAll('[data-camera]').forEach(button => button.addEventListener('click', () => setCamera(button.dataset.camera)));
  ui.exportPatch.addEventListener('click', exportPatchCsv);
  ui.installResolume.addEventListener('click', () => {
    hostPost({ type: 'installResolume' });
    ui.installStatus.textContent = 'Copiando fixtures y preset…';
    ui.installStatus.className = 'install-status';
  });
  if (!MINIMAL_VIEW) renderer.domElement.addEventListener('pointerup', pickPixel);
}

function onResize() {
  camera.aspect = ui.viewport.clientWidth / Math.max(1, ui.viewport.clientHeight);
  camera.updateProjectionMatrix();
  renderer.setSize(ui.viewport.clientWidth, ui.viewport.clientHeight);
}

window.addEventListener('resize', onResize);
if (window.chrome?.webview) window.chrome.webview.addEventListener('message', event => handleHostMessage(event.data));

buildEnvironment();
buildStand();
buildFlagFabric();
buildMappedElements();
buildBannerBackings();
const ceilingInitialOverrides = {};
for (const [groupId, fallbackSide, group] of [
  ['CEILING-LEFT', -1, ceilingLeftGroup],
  ['CEILING-RIGHT', 1, ceilingRightGroup]
]) {
  const portableGroup = VISUAL_LAYOUT?.ceiling?.groups?.[groupId];
  const side = portableGroup?.side === 'left' ? -1 : portableGroup?.side === 'right' ? 1 : fallbackSide;
  for (let i = 0; i < CEILING_LAYOUT.anchorY.length; i++) {
    const support = cylinderBetween([side * CEILING_LAYOUT.outerX, CEILING_LAYOUT.anchorY[i] + ceilingRoofRise(CEILING_LAYOUT.outerX), CEILING_LAYOUT.anchorZ[i]],
      [side * CEILING_LAYOUT.innerX, CEILING_LAYOUT.anchorY[i], CEILING_LAYOUT.anchorZ[i]], 0.006, materials.rack, group);
    support.name = `${CEILING_SUPPORTS[i]} · ${side < 0 ? 'izquierda' : 'derecha'}`;
    support.userData.supportId = CEILING_SUPPORTS[i];
    support.userData.supportRail = true;
  }
  const pivot = numericTriple(portableGroup?.pivot_m) || [side * 2.1, CEILING_PIVOT_Y, 0];
  editableObjects.push({ id: groupId,
    label: `Colgantes techo ${side < 0 ? 'izquierdo' : 'derecho'} · ${CEILING_LAYOUT.perSide} tiras de 5 m`,
    parent: ceilingGroup, members: [group], pivot: new THREE.Vector3(...pivot) });
  ceilingInitialOverrides[groupId] = sceneTransform(portableGroup?.transform, { ry: 180 });
}
// Keep each mapped strand and its saved local transform inside a single editable tree.
editableObjects.push({ id: 'CENTRAL-TREE', label: 'Árbol central completo · 24 aristas',
  isLayerVisible: () => ui.showCentral.checked,
  parent: world, members: [centralGroup], pivot: new THREE.Vector3(0, CONFIG.central_vertical_height_m / 2, 0) });
editableObjects.push({ id: 'STAND', label: 'Estructura física completa', parent: world, members: [structureGroup] });
sceneEditor = createSceneEditor({ THREE, scene, world, objects: editableObjects, state, renderer, camera, showNotice,
  initialOverrides: { ...CURRENT_LAYOUT, ...waveInitialOverrides, ...flagInitialOverrides, ...ceilingInitialOverrides },
  initialDocument: null,
  readOnly: true
});
bindUi();
updateLayerVisibility();
if (!window.chrome?.webview) {
  ui.ndiRefresh.disabled = true;
  ui.ndiSource.disabled = true;
  ui.installResolume.disabled = true;
  if (MINIMAL_VIEW) {
    ui.mode.value = 'artnet';
    artNetState.livePreview = true;
    artNetState.outputSignal = false;
    ui.modeLabel.textContent = 'Vista sin motor · abre LIGHTMAN V20 MINIMAL';
  } else {
    ui.mode.value = 'simulation';
    ui.installStatus.textContent = 'Vista web: para instalar el patch utiliza el ejecutable.';
    ui.ndiStatus.textContent = 'Vista web local: NDI y recepción Art-Net disponibles en el .exe.';
    ui.mode.querySelector('[value="artnet"]').disabled = true;
  }
}
updateMode();
updatePointColors();
hostPost({ type: 'ready' });

requestAnimationFrame(() => {
  ui.loading.classList.add('hidden');
  setTimeout(() => ui.loading.remove(), 500);
});

const clock = new THREE.Clock();
let lastColorUpdate = 0;

function animate(now) {
  requestAnimationFrame(animate);
  const delta = Math.min(0.05, clock.getDelta());
  if (state.playing) state.elapsed += delta * state.speed;
  updateCameraTween(now);
  controls.update();
  sceneEditor.update();
  if (now - lastColorUpdate > 32) {
    if (state.mode === 'simulation' || !artNetIsLive()) updatePointColors();
    updateArtNetUi();
    lastColorUpdate = now;
  }
  renderer.render(scene, camera);
}

requestAnimationFrame(animate);
