// Shared geometry for the preview and the full-size printable mounting template.
export const BANNER = { width: 2.48, height: 2.05, pathWidth: 2.4146, length: 5, mountLength: 4.9, overhang: .05,
  rows: 4, pixels: 100, diameter: 0.025, margin: 0.025, centerX: 1.65, z: -0.965 };
const samples = 1200;
const filletRadius=.055,posts=Array.from({length:5},(_,i)=>-BANNER.pathWidth/2+i*BANNER.pathWidth/4);
const rounded=amplitude=>{const vertices=posts.map((x,i)=>[x,i%2?amplitude:0]),out=[vertices[0]],unit=(a,b)=>{const l=Math.hypot(b[0]-a[0],b[1]-a[1]);return[(b[0]-a[0])/l,(b[1]-a[1])/l]};for(let i=1;i<4;i++){const q=vertices[i],u=unit(vertices[i-1],q),v=unit(q,vertices[i+1]),turn=Math.atan2(u[0]*v[1]-u[1]*v[0],u[0]*v[0]+u[1]*v[1]),t=filletRadius*Math.abs(Math.tan(turn/2)),a=[q[0]-u[0]*t,q[1]-u[1]*t],b=[q[0]+v[0]*t,q[1]+v[1]*t],sg=Math.sign(turn),c=[a[0]-u[1]*sg*filletRadius,a[1]+u[0]*sg*filletRadius],start=Math.atan2(a[1]-c[1],a[0]-c[0]);out.push(a);for(let j=1;j<=18;j++){const z=start+turn*j/18;out.push([c[0]+Math.cos(z)*filletRadius,c[1]+Math.sin(z)*filletRadius])}out[out.length-1]=b}out.push(vertices[4]);return out};
const length = points => points.slice(1).reduce((sum, p, i) => sum + Math.hypot(p[0] - points[i][0], p[1] - points[i][1]), 0);
// The required 5 m path needs about 1.174 m of amplitude.  A 1 m upper
// bound shortened every rendered neon to about 4.465 m and left material
// over at both ends, so keep enough search range for the physical length.
let low = 0, high = 1.6;
for (let i = 0; i < 45; i++) {
  const mid = (low + high) / 2;
  if (length(rounded(mid)) < BANNER.mountLength) low = mid; else high = mid;
}
export const WAVE_AMPLITUDE = (low + high) / 2;
export const WAVE_BASE_Y=.06;
export const WAVE_SPACING=.52;
const mountedPath=rounded(WAVE_AMPLITUDE);
const extendEnd=(p,q,d)=>{const l=Math.hypot(q[0]-p[0],q[1]-p[1]);return [p[0]+(p[0]-q[0])*d/l,p[1]+(p[1]-q[1])*d/l]};
// Five centimetres remain beyond P1 and P5.  The four fixed endpoint levels
// are 6, 149, 58 and 201 cm, as requested for posts 1, 3 and 5.
export const WAVE_PATH=[extendEnd(mountedPath[0],mountedPath[1],BANNER.overhang),...mountedPath,extendEnd(mountedPath.at(-1),mountedPath.at(-2),BANNER.overhang)];
const placePath=(row,[x,y])=>[x+BANNER.width/2,
  row===0?y+.06:row===1?1.49-y:row===2?y+.58:2.01-y];
export const BANNER_PATHS=Array.from({length:BANNER.rows},(_,row)=>WAVE_PATH.map(point=>placePath(row,point)));
export const BANNER_ANCHORS=[];
// Preserve any user displacement while replacing the previous 2.5-cycle curves.
export const PREVIOUS_WAVE_BASE_Y = 0.4705978610325957;
export const PREVIOUS_WAVE_SPACING = 0.4117608555869617;

export function mirrorTransform(t) {
  // Reflection across the center plane: S RxRyRz S = Rx(-Ry)(-Rz).
  return { ...t, x: -t.x || 0, ry: -t.ry || 0, rz: -t.rz || 0 };
}
// Physical slots read from the user's frontal layout.  FLAG-N selects one of
// these slots through visual_slot in patch-map; signal routes are never swapped.
const LEFT_UPPER_OUTER = { x: -2.582, y: 2.398, z: 0.3904, rx: 45, ry: 45, rz: 0 };
const LEFT_UPPER_INNER = { x: -1.314, y: 3.114, z: 0.334, rx: 45, ry: 45, rz: 0 };
const LEFT_LOWER = { x: -2.9824, y: 0.922, z: 0.6584, rx: 0, ry: 0, rz: 0 };
export const FLAG_SLOT_TRANSFORMS = Object.freeze({
  'upper-left-outer': LEFT_UPPER_OUTER,
  'upper-left-inner': LEFT_UPPER_INNER,
  'upper-right-inner': mirrorTransform(LEFT_UPPER_INNER),
  'upper-right-outer': mirrorTransform(LEFT_UPPER_OUTER),
  'lower-left': LEFT_LOWER,
  'lower-right': mirrorTransform(LEFT_LOWER)
});
export const BANNER_OVERRIDES = Object.fromEntries(['WAVE-LEFT', 'WAVE-RIGHT'].map((id, i) => [id,
  { x: (i ? 1 : -1) * BANNER.centerX, y: 0, z: BANNER.z,
    rx: 0, ry: 0, rz: 0, count: 1, spacing: WAVE_SPACING, axis: 'Y' }]));
export const CURRENT_LAYOUT = {
  ...BANNER_OVERRIDES,
  'TV-L': { x: 0.046, y: 0.8617, z: 0.471, count: 1 }, 'TV-R': { count: 0 }
};
