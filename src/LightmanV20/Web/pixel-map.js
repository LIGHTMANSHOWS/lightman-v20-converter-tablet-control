// Convert one visual pixel into its unchanged source-route pixel.
// Orientation corrections happen before the physical wiring transform so the
// viewer can be mirrored/rotated without changing Art-Net, IPs or geometry.
export function sourcePixelFor(element, visualPixel) {
  const portable = element.visual?.pixel_mapping;
  const routeOffset = Number(portable?.route_offset_pixels ?? element.route_offset ?? 0);
  const routePixel = portable?.route_pixel_indices?.[visualPixel];
  if (Number.isInteger(routePixel) && Number.isFinite(routeOffset)) {
    const localPixel = routePixel - routeOffset;
    if (localPixel >= 0 && localPixel < element.pixel_count) return localPixel;
  }

  let mappedPixel = visualPixel;
  const transforms = Array.isArray(portable?.visual_pixel_transform)
    ? portable.visual_pixel_transform
    : [
        ...(element.visual_flip_x ? [{ operation: 'flip-x', row_width: 100 }] : []),
        ...(element.visual_rotate_180 ? [{ operation: 'rotate-180', width: 60, height: 42 }] : [])
      ];
  for (const transform of transforms) {
    if (transform?.operation === 'flip-x') {
      const rowWidth = Number(transform.row_width || 100);
      const row = Math.floor(mappedPixel / rowWidth);
      mappedPixel = row * rowWidth + (rowWidth - 1 - mappedPixel % rowWidth);
    } else if (transform?.operation === 'rotate-180') {
      const width = Number(transform.width || 60);
      const height = Number(transform.height || 42);
      const x = mappedPixel % width;
      const y = Math.floor(mappedPixel / width);
      mappedPixel = (height - 1 - y) * width + (width - 1 - x);
    }
  }

  const wiring = portable?.physical_wiring_transform || element.direction;
  if (wiring === 'reverse') return element.pixel_count - 1 - mappedPixel;
  if (wiring === 'row-snake-100') {
    const row = Math.floor(mappedPixel / 100);
    return row * 100 + (row % 2 ? 99 - mappedPixel % 100 : mappedPixel % 100);
  }
  if (wiring === 'row-snake-100-right') {
    const row = Math.floor(mappedPixel / 100);
    return row * 100 + (row % 2 ? mappedPixel % 100 : 99 - mappedPixel % 100);
  }
  if (wiring === 'matrix-snake-14') {
    const x = mappedPixel % 60;
    const y = Math.floor(mappedPixel / 60);
    const output = Math.floor(y / 14);
    const localY = y % 14;
    const physicalX = localY % 2 === 0 ? x : 59 - x;
    return output * 840 + localY * 60 + physicalX;
  }
  return mappedPixel;
}
