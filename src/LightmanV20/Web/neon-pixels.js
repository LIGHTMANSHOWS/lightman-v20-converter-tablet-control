import * as THREE from './three.module.min.js';

// Each addressed LED is one physical 50 mm x Ø25 mm luminous cylinder.
export function createNeonPixels(path, count) {
  const cumulative = [0];
  for (let i = 1; i < path.length; i++) cumulative.push(cumulative[i - 1] + path[i].distanceTo(path[i - 1]));
  const total = cumulative.at(-1);
  if (!(total > 0) || !Number.isInteger(count) || count < 1) throw new Error('Trayectoria de neón inválida.');
  function sample(distance) {
    let lo = 1, hi = cumulative.length - 1;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (cumulative[mid] < distance) lo = mid + 1; else hi = mid; }
    const span = cumulative[lo] - cumulative[lo - 1];
    return new THREE.Vector3().lerpVectors(path[lo - 1], path[lo], span > 0 ? (distance - cumulative[lo - 1]) / span : 0);
  }
  const geometry = new THREE.CylinderGeometry(0.0125, 0.0125, 0.05, 12);
  const material = new THREE.MeshBasicMaterial({ color: 0xffffff, toneMapped: false });
  const mesh = new THREE.InstancedMesh(geometry, material, count);
  const colors = new Float32Array(count * 3).fill(1);
  mesh.instanceColor = new THREE.InstancedBufferAttribute(colors, 3);
  mesh.instanceColor.setUsage(THREE.DynamicDrawUsage);
  mesh.userData.neon = { length: 0.05, diameter: 0.025, pathLength: total };
  const points = [], up = new THREE.Vector3(0, 1, 0), scale = new THREE.Vector3(1, 1, 1);
  const matrix = new THREE.Matrix4(), rotation = new THREE.Quaternion();
  for (let i = 0; i < count; i++) {
    const a = sample(total * i / count), b = sample(total * (i + 1) / count);
    const midpoint = sample(total * (i + 0.5) / count);
    rotation.setFromUnitVectors(up, b.sub(a).normalize());
    matrix.compose(midpoint, rotation, scale); mesh.setMatrixAt(i, matrix); points.push(midpoint);
  }
  mesh.instanceMatrix.needsUpdate = true;
  mesh.computeBoundingBox(); mesh.computeBoundingSphere();
  mesh.frustumCulled = false;
  return { mesh, geometry, colors, points };
}

// Separate runs share one address buffer, but never join across the air gap.
export function createNeonRuns(paths, pixelsPerRun) {
  const runs = paths.map(path => createNeonPixels(path, pixelsPerRun));
  const count = paths.length * pixelsPerRun;
  const mesh = new THREE.InstancedMesh(runs[0].geometry, runs[0].mesh.material, count);
  const colors = new Float32Array(count * 3).fill(1), points = [];
  mesh.instanceColor = new THREE.InstancedBufferAttribute(colors, 3);
  mesh.instanceColor.setUsage(THREE.DynamicDrawUsage);
  const matrix = new THREE.Matrix4();
  runs.forEach((run, row) => {
    for (let p = 0; p < pixelsPerRun; p++) {
      run.mesh.getMatrixAt(p, matrix); mesh.setMatrixAt(row * pixelsPerRun + p, matrix);
    }
    points.push(...run.points);
    if (row) { run.geometry.dispose(); run.mesh.material.dispose(); }
  });
  mesh.instanceMatrix.needsUpdate = true;
  mesh.computeBoundingBox(); mesh.computeBoundingSphere(); mesh.frustumCulled = false;
  mesh.userData.neon = { length: 0.05, diameter: 0.025, runs: paths.length, pathLength: 5 };
  return { mesh, geometry: runs[0].geometry, colors, points };
}
