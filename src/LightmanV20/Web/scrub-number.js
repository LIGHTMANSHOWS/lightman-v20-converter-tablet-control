// Absolute pointer displacement: stopping holds the value; returning restores it.
export function bindScrubNumber(input, { rate, change, finish, cancel }) {
  let drag = null;
  input.classList.add('scrub-number');
  input.title = 'Arrastra arriba/abajo (o izquierda/derecha) · Shift: fino · Ctrl: rápido · Esc: cancelar';
  input.addEventListener('input', () => {
    if (input.value.trim() && Number.isFinite(input.valueAsNumber)) change(input.valueAsNumber);
  });
  input.addEventListener('blur', () => { if (!drag) finish(); });
  input.addEventListener('keydown', event => {
    if (event.key === 'Enter') { event.preventDefault(); input.blur(); }
    if (event.key === 'Escape') {
      event.preventDefault(); event.stopPropagation();
      const id = drag?.id; drag = null;
      input.classList.remove('scrubbing');
      cancel();
      if (id !== undefined && input.hasPointerCapture(id)) input.releasePointerCapture(id);
      input.blur();
    }
  });
  input.addEventListener('pointerdown', event => {
    if (event.button !== 0) return;
    event.preventDefault();
    input.focus();
    const value = Number(input.value) || 0;
    drag = { id: event.pointerId, startX: event.clientX, startY: event.clientY,
      axis: null, anchor: 0, last: 0, anchorValue: value, value,
      factor: event.shiftKey ? 0.1 : event.ctrlKey ? 10 : 1, moved: false };
    input.setPointerCapture(event.pointerId);
  });
  input.addEventListener('pointermove', event => {
    if (!drag || drag.id !== event.pointerId) return;
    event.preventDefault();
    const dx = event.clientX - drag.startX, dy = event.clientY - drag.startY;
    if (!drag.axis) {
      if (Math.max(Math.abs(dx), Math.abs(dy)) < 3) return;
      // Lock the first deliberate direction to avoid diagonal jitter.
      drag.axis = Math.abs(dy) >= Math.abs(dx) ? 'y' : 'x';
      drag.anchor = drag.last = drag.axis === 'y' ? -drag.startY : drag.startX;
    }
    drag.moved = true; input.classList.add('scrubbing');
    const position = drag.axis === 'y' ? -event.clientY : event.clientX;
    const factor = event.shiftKey ? 0.1 : event.ctrlKey ? 10 : 1;
    if (factor !== drag.factor) {
      drag.anchorValue = drag.value; drag.anchor = drag.last; drag.factor = factor;
    }
    drag.value = Math.min(Number(input.max), Math.max(Number(input.min), drag.anchorValue + (position - drag.anchor) * rate * factor));
    drag.last = position;
    const value = input.dataset.integer === 'true' ? Math.round(drag.value) : Number(drag.value.toFixed(4));
    if (change(value) !== false) input.value = value;
  });
  function end(event, aborted = false) {
    if (!drag || drag.id !== event.pointerId) return;
    const wasMoved = drag.moved; drag = null;
    input.classList.remove('scrubbing');
    if (aborted) cancel(); else if (wasMoved) finish(); else input.select();
    if (input.hasPointerCapture(event.pointerId)) input.releasePointerCapture(event.pointerId);
  }
  input.addEventListener('pointerup', event => end(event));
  input.addEventListener('pointercancel', event => end(event, true));
  input.addEventListener('lostpointercapture', event => end(event));
}
