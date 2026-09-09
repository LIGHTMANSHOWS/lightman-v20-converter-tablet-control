// Three hanging spans on ONE continuous 5 m / 100 px Neon Flex strip.
// Coordinates: Y height from floor, Z back (-) to front (+), X left/right.
export const CEILING_LAYOUT = Object.freeze({ perSide: 8, length: 5, pixels: 100,
  outerX: 3.30, innerX: 0.90, anchorY: [5 - 3.30 / 3.5, 10 / 3, 8 / 3, 2], anchorZ: [-1, -1 / 3, 1 / 3, 1] });
// After the saved RY=180 orientation: SC1 is front Z=+1 on the gable,
// SC4 is back Z=-1. SC2/SC3 divide the 2 m depth into three equal spans.
export const CEILING_SUPPORTS = ['SC1', 'SC2', 'SC3', 'SC4'];
export const CEILING_SPACING = (CEILING_LAYOUT.outerX - CEILING_LAYOUT.innerX) / (CEILING_LAYOUT.perSide - 1);
export const CEILING_RISE_PER_STRIP = CEILING_SPACING / 3.5;
export const CEILING_PIVOT_Y = 2.7023936175515635; // Stable revision-7 pivot; do not move saved assemblies.
export const ceilingRoofRise = x => (Math.abs(x) - CEILING_LAYOUT.innerX) / 3.5;
// The saved assemblies face RY=180: their X order reverses. Build the rise in
// local space so the FINAL world-space strips rise toward the ridge, not away.

function hangingSpan(z0, y0, z1, y1, length) {
  const width = z1 - z0, rise = y1 - y0;
  if (length <= Math.hypot(width, rise)) throw new Error('No queda longitud para la caída del neón.');
  const q = Math.sqrt(length * length - rise * rise);
  let low = width / 100, high = 100;
  for (let i = 0; i < 70; i++) {
    const a = (low + high) / 2;
    if (2 * a * Math.sinh(width / (2 * a)) > q) low = a; else high = a;
  }
  const a = (low + high) / 2;
  const bottom = width / 2 - a * Math.asinh(rise / q);
  return Array.from({ length: 601 }, (_, i) => {
    const z = width * i / 600;
    return [y0 + a * (Math.cosh((z - bottom) / a) - Math.cosh(bottom / a)), z0 + z];
  });
}
const spanCount = CEILING_LAYOUT.anchorY.length - 1;
const profile = Array.from({ length: spanCount }, (_, i) => {
  const span = hangingSpan(CEILING_LAYOUT.anchorZ[i], CEILING_LAYOUT.anchorY[i],
    CEILING_LAYOUT.anchorZ[i + 1], CEILING_LAYOUT.anchorY[i + 1], CEILING_LAYOUT.length / spanCount);
  return i ? span.slice(1) : span;
}).flat();
export function ceilingPlacement(index) {
  const n = index % 20, side = n < 10 ? -1 : 1, slot = n % 10;
  // Keep the right side in its existing Art-Net slots 11-18, not 9-16.
  const active = index < 20 && slot < CEILING_LAYOUT.perSide;
  return { active, side, slot, x: side * (CEILING_LAYOUT.outerX - Math.min(slot, CEILING_LAYOUT.perSide - 1) * CEILING_SPACING) };
}
export function ceilingPath(index) {
  const { x } = ceilingPlacement(index);
  return profile.map(([y, z]) => [x, y + ceilingRoofRise(x), z]);
}
export function ceilingAnchors(index) {
  const { x } = ceilingPlacement(index);
  return CEILING_LAYOUT.anchorY.map((y, i) => [x, y + ceilingRoofRise(x), CEILING_LAYOUT.anchorZ[i]]);
}
