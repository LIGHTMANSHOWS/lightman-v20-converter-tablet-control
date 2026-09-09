'use strict';

const POLL_INTERVAL_MS = 1500;
const REQUEST_TIMEOUT_MS = 4500;

const FALLBACK_CATALOG = Object.freeze([
  { id: 'skeewiff', publicTitle: 'Ritmo de Luz', tagline: 'Color, movimiento y una entrada con energía.', category: 'Experiencia visual' },
  { id: 'santa', publicTitle: 'La Voz de Santa', tagline: 'Un encuentro navideño contado en español.', category: 'Navidad' },
  { id: 'sarajevo', publicTitle: 'Noche Eléctrica', tagline: 'Una tormenta sinfónica de luz y emoción.', category: 'Arquitectura de luz' },
  { id: 'winter-wizard', publicTitle: 'Hechizo de Invierno', tagline: 'Magia invernal que recorre todo el espacio.', category: 'Arquitectura de luz' },
  { id: 'miser-brothers', publicTitle: 'Fuego & Hielo', tagline: 'Dos fuerzas opuestas frente a frente.', category: 'Experiencia visual' },
  { id: 'here-comes-santa-claus', publicTitle: 'La Llegada de Santa', tagline: 'La espera termina: Santa está por llegar.', category: 'Navidad' },
  { id: 'sleigh-ride-8bit', publicTitle: 'Trineo Pixel', tagline: 'Un paseo navideño con alma de videojuego.', category: 'Navidad' },
  { id: 'this-is-halloween', publicTitle: 'Noche de Halloween', tagline: 'Una noche traviesa de sombras, color y personajes.', category: 'Especial de Halloween' },
  { id: 'light-em-up', publicTitle: 'Enciende la Noche', tagline: 'Fuego, ritmo y energía para iluminar la oscuridad.', category: 'Experiencia visual' },
  { id: 'baby-shark-edm', publicTitle: 'Océano Eléctrico', tagline: 'Una fiesta submarina para bailar en familia.', category: 'Experiencia familiar' },
  { id: 'blinding-lights', publicTitle: 'Ciudad de Neón', tagline: 'Un viaje nocturno entre destellos y velocidad.', category: 'Experiencia visual' },
  { id: 'believer', publicTitle: 'Fuerza Imparable', tagline: 'Golpes de luz que convierten la energía en fuerza.', category: 'Experiencia visual' },
  { id: 'uptown-funk', publicTitle: 'Ritmo en la Ciudad', tagline: 'Brillo, actitud y ritmo para encender la ciudad.', category: 'Experiencia visual' }
]);

const VISUALS = Object.freeze([
  { theme: '', symbol: '♪' },
  { theme: 'theme-gold', symbol: '✦' },
  { theme: 'theme-coral', symbol: '◆' },
  { theme: 'theme-violet', symbol: '❄' },
  { theme: 'theme-blue', symbol: '◐' },
  { theme: 'theme-pink', symbol: '★' },
  { theme: 'theme-mint', symbol: '▦' }
]);

const CATEGORY_ORDER = Object.freeze([
  'Arquitectura de luz',
  'Experiencia visual',
  'Experiencia familiar',
  'Especial de Halloween',
  'Navidad'
]);

const fallbackById = new Map(FALLBACK_CATALOG.map((item, index) => [item.id, { ...item, visualIndex: index }]));
const elements = {
  availability: document.getElementById('availability'),
  availabilityLabel: document.getElementById('availability-label'),
  fullscreenToggle: document.getElementById('fullscreen-toggle'),
  fullscreenLabel: document.getElementById('fullscreen-label'),
  trackingSection: document.getElementById('tracking-section'),
  trackingStatus: document.getElementById('tracking-status'),
  trackingStatusLabel: document.getElementById('tracking-status-label'),
  trackingModes: document.getElementById('tracking-modes'),
  categories: document.getElementById('categories'),
  catalog: document.getElementById('catalog'),
  catalogTitle: document.getElementById('catalog-title'),
  actionStatus: document.getElementById('action-status'),
  emptyState: document.getElementById('empty-state'),
  emptyMessage: document.getElementById('empty-message'),
  retry: document.getElementById('retry'),
  successDialog: document.getElementById('success-dialog'),
  successTitle: document.getElementById('success-title'),
  successMessage: document.getElementById('success-message')
};

const state = {
  experiences: [],
  tracking: null,
  category: 'Todas',
  busyId: null,
  trackingBusyId: null,
  activeId: null,
  pendingExperienceId: null,
  pendingExperienceAt: 0,
  unavailable: false,
  hasLoaded: false,
  failures: 0,
  catalogSignature: '',
  trackingSignature: ''
};

let pollTimer = 0;
let requestInFlight = false;
let fullscreenRequestPending = false;

function cleanText(value, fallback = '') {
  return typeof value === 'string' && value.trim() ? value.trim() : fallback;
}

function normaliseExperience(item, index) {
  if (!item || typeof item !== 'object') return null;
  const id = cleanText(item.id);
  const known = fallbackById.get(id);
  if (!id) return null;

  const publicTitle = cleanText(item.publicTitle, known?.publicTitle);
  const tagline = cleanText(item.tagline, known?.tagline);
  const category = cleanText(item.category, known?.category || 'Experiencias');
  if (!publicTitle || !tagline) return null;

  return {
    id,
    publicTitle,
    tagline,
    category,
    enabled: item.enabled !== false,
    visualIndex: known?.visualIndex ?? index
  };
}

function normaliseTracking(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return null;

  const modes = Array.isArray(value.modes)
    ? value.modes.map(item => {
      if (!item || typeof item !== 'object') return null;
      const id = cleanText(item.id);
      const title = cleanText(item.title);
      if (!id || !title) return null;
      return {
        id,
        title,
        description: cleanText(item.description),
        enabled: item.enabled !== false
      };
    }).filter(Boolean)
    : [];

  return {
    configured: value.configured === true,
    connected: value.connected === true,
    status: cleanText(value.status),
    desiredMode: cleanText(value.desiredMode) || null,
    activeMode: cleanText(value.activeMode) || null,
    modes
  };
}

function setAvailability(mode, label) {
  elements.availability.classList.toggle('is-ready', mode === 'ready');
  elements.availability.classList.toggle('is-unavailable', mode === 'unavailable');
  elements.availabilityLabel.textContent = label;
}

function setActionStatus(message, tone = '') {
  elements.actionStatus.textContent = message;
  elements.actionStatus.className = `action-status${tone ? ` is-${tone}` : ''}`;
}

function getCategories() {
  const unique = [];
  for (const item of state.experiences) {
    if (!unique.includes(item.category)) unique.push(item.category);
  }
  unique.sort((left, right) => {
    const leftIndex = CATEGORY_ORDER.indexOf(left);
    const rightIndex = CATEGORY_ORDER.indexOf(right);
    if (leftIndex < 0 && rightIndex < 0) return left.localeCompare(right, 'es');
    if (leftIndex < 0) return 1;
    if (rightIndex < 0) return -1;
    return leftIndex - rightIndex;
  });
  return ['Todas', ...unique];
}

function renderCategories() {
  const categories = getCategories();
  if (!categories.includes(state.category)) state.category = 'Todas';

  const buttons = categories.map(category => {
    const button = document.createElement('button');
    button.className = 'category-button';
    button.type = 'button';
    button.textContent = category;
    button.setAttribute('aria-pressed', String(category === state.category));
    button.addEventListener('click', () => {
      state.category = category;
      renderCategories();
      renderCatalog();
    });
    return button;
  });

  elements.categories.replaceChildren(...buttons);
}

function createExperienceCard(item, index) {
  const visual = VISUALS[item.visualIndex % VISUALS.length];
  const card = document.createElement('button');
  card.type = 'button';
  card.className = `experience-card ${visual.theme}`.trim();
  card.dataset.experienceId = item.id;
  card.disabled = state.unavailable || state.busyId !== null || state.trackingBusyId !== null || !item.enabled;
  card.classList.toggle('is-unavailable', !item.enabled || state.unavailable);
  card.classList.toggle('is-busy', state.busyId === item.id);
  card.classList.toggle('is-active', state.activeId === item.id);
  card.setAttribute('aria-pressed', String(state.activeId === item.id));
  card.setAttribute('aria-label', `${state.busyId === item.id ? 'Iniciando' : item.enabled ? 'Vivir' : 'Próximamente'} ${item.publicTitle}. ${item.tagline}`);

  const top = document.createElement('span');
  top.className = 'card-top';

  const symbol = document.createElement('span');
  symbol.className = 'card-symbol';
  symbol.setAttribute('aria-hidden', 'true');
  symbol.textContent = visual.symbol;

  const marker = document.createElement('span');
  if (state.activeId === item.id) {
    marker.className = 'card-badge';
    marker.textContent = 'En escena';
  } else {
    marker.className = 'card-number';
    marker.textContent = String(index + 1).padStart(2, '0');
  }
  top.append(symbol, marker);

  const copy = document.createElement('span');
  copy.className = 'card-copy';

  const category = document.createElement('span');
  category.className = 'card-category';
  category.textContent = item.category;

  const title = document.createElement('span');
  title.className = 'card-title';
  title.textContent = item.publicTitle;

  const tagline = document.createElement('span');
  tagline.className = 'card-tagline';
  tagline.textContent = item.tagline;
  copy.append(category, title, tagline);

  const action = document.createElement('span');
  action.className = 'card-action';
  const actionLabel = document.createElement('span');
  actionLabel.textContent = state.busyId === item.id ? 'Iniciando…' : item.enabled ? 'Vivir ahora' : 'Próximamente';
  const arrow = document.createElement('span');
  arrow.className = 'card-action__arrow';
  arrow.setAttribute('aria-hidden', 'true');
  arrow.textContent = state.busyId === item.id ? '·' : '→';
  action.append(actionLabel, arrow);

  card.append(top, copy, action);
  card.addEventListener('click', () => playExperience(item));
  return card;
}

function renderCatalog() {
  const visible = state.category === 'Todas'
    ? state.experiences
    : state.experiences.filter(item => item.category === state.category);

  elements.catalogTitle.textContent = state.category;
  elements.catalog.setAttribute('aria-busy', String(state.busyId !== null));
  elements.catalog.hidden = visible.length === 0;
  elements.emptyState.hidden = visible.length !== 0;
  elements.retry.hidden = !state.unavailable;
  elements.emptyMessage.textContent = state.unavailable
    ? 'Las experiencias no están disponibles por el momento.'
    : 'No hay experiencias disponibles en esta categoría.';

  if (visible.length) {
    elements.catalog.replaceChildren(...visible.map(createExperienceCard));
  } else {
    elements.catalog.replaceChildren();
  }
}

function trackingStatusLabel(tracking) {
  const status = tracking.status.toLocaleLowerCase('es');
  if (status === 'error') return 'No disponible';
  if (status === 'degraded' && tracking.activeMode) return 'En vivo · limitado';
  if (tracking.activeMode) return 'En vivo';
  if (status === 'starting') return 'Preparando';
  if (tracking.connected) return 'Listo para elegir';
  if (!tracking.configured) return 'Próximamente';

  if (status.includes('prepar') || status.includes('conect')) return 'Preparando';
  return 'En espera';
}

function createTrackingMode(mode) {
  const tracking = state.tracking;
  const isActive = tracking.activeMode === mode.id;
  const isDesired = tracking.desiredMode === mode.id;
  const isBusy = state.trackingBusyId === mode.id;

  const button = document.createElement('button');
  button.type = 'button';
  button.className = 'tracking-mode';
  button.dataset.trackingMode = mode.id;
  button.classList.toggle('is-active', isActive);
  button.classList.toggle('is-selected', !isActive && isDesired);
  button.disabled = !tracking.configured || !mode.enabled || state.busyId !== null || state.trackingBusyId !== null;
  button.setAttribute('aria-pressed', String(isActive || isDesired));
  button.setAttribute('aria-label', `${isActive ? 'En vivo' : isBusy || isDesired ? 'Preparando' : mode.enabled ? 'Elegir' : 'Próximamente'}: ${mode.title}${mode.description ? `. ${mode.description}` : ''}`);

  const copy = document.createElement('span');
  copy.className = 'tracking-mode__copy';
  const title = document.createElement('span');
  title.className = 'tracking-mode__title';
  title.textContent = mode.title;
  copy.append(title);
  if (mode.description) {
    const description = document.createElement('span');
    description.className = 'tracking-mode__description';
    description.textContent = mode.description;
    copy.append(description);
  }

  const marker = document.createElement('span');
  marker.className = 'tracking-mode__state';
  marker.textContent = isActive ? 'En vivo' : isBusy || isDesired ? 'Preparando' : mode.enabled && tracking.configured ? 'Elegir' : 'Próximo';

  button.append(copy, marker);
  button.addEventListener('click', () => selectTrackingMode(mode));
  return button;
}

function renderTracking() {
  const tracking = state.tracking;
  elements.trackingSection.hidden = tracking === null;
  if (!tracking) {
    elements.trackingModes.replaceChildren();
    return;
  }

  elements.trackingStatus.classList.toggle('is-connected', tracking.connected && !tracking.activeMode);
  elements.trackingStatus.classList.toggle('is-active', Boolean(tracking.activeMode));
  elements.trackingStatus.classList.toggle('is-unavailable', tracking.status === 'error');
  elements.trackingStatusLabel.textContent = trackingStatusLabel(tracking);
  elements.trackingModes.setAttribute('aria-busy', String(state.trackingBusyId !== null));

  if (tracking.modes.length === 0) {
    const empty = document.createElement('p');
    empty.className = 'tracking-empty';
    empty.textContent = 'Las interacciones estarán disponibles pronto.';
    elements.trackingModes.replaceChildren(empty);
    return;
  }

  elements.trackingModes.replaceChildren(...tracking.modes.map(createTrackingMode));
}

function experienceSignature(items) {
  return JSON.stringify(items.map(item => [item.id, item.publicTitle, item.tagline, item.category, item.enabled, item.visualIndex]));
}

function trackingSignature(tracking) {
  if (!tracking) return '';
  return JSON.stringify([
    tracking.configured,
    tracking.connected,
    tracking.status,
    tracking.desiredMode,
    tracking.activeMode,
    tracking.modes.map(mode => [mode.id, mode.title, mode.description, mode.enabled])
  ]);
}

function updateReadyMessage() {
  const readyCount = state.experiences.filter(item => item.enabled).length;
  const upcomingCount = state.experiences.length - readyCount;
  const readyLabel = `${readyCount} ${readyCount === 1 ? 'experiencia lista' : 'experiencias listas'}`;
  setActionStatus(upcomingCount > 0 ? `${readyLabel} · ${upcomingCount} próximamente` : readyLabel);
}

function applyPayload(payload, initial) {
  const source = Array.isArray(payload) ? payload : payload?.experiences;
  if (!Array.isArray(source)) throw new Error('invalid');

  const experiences = source.map(normaliseExperience).filter(Boolean);
  const tracking = normaliseTracking(Array.isArray(payload) ? null : payload.tracking);
  const reportedActiveId = !Array.isArray(payload) && Object.prototype.hasOwnProperty.call(payload, 'activeExperienceId')
    ? payload.activeExperienceId
    : !Array.isArray(payload) ? payload.activeId : null;
  const activeId = cleanText(reportedActiveId) || null;
  const nextCatalogSignature = experienceSignature(experiences);
  const nextTrackingSignature = trackingSignature(tracking);
  const catalogChanged = nextCatalogSignature !== state.catalogSignature;
  const trackingChanged = nextTrackingSignature !== state.trackingSignature;
  const activeChanged = activeId !== state.activeId;
  const previousTrackingMode = state.tracking?.activeMode || null;
  const pendingConfirmed = Boolean(state.pendingExperienceId) && activeId === state.pendingExperienceId;
  const pendingExpired = Boolean(state.pendingExperienceId) &&
    Date.now() - state.pendingExperienceAt >= 10000;

  state.experiences = experiences;
  state.tracking = tracking;
  state.activeId = activeId;
  state.unavailable = false;
  state.hasLoaded = true;
  state.failures = 0;
  state.catalogSignature = nextCatalogSignature;
  state.trackingSignature = nextTrackingSignature;
  if (pendingConfirmed || pendingExpired) {
    state.pendingExperienceId = null;
    state.pendingExperienceAt = 0;
  }

  setAvailability('ready', 'Listo');

  if (catalogChanged || initial) renderCategories();
  if (catalogChanged || activeChanged || initial) renderCatalog();
  if (trackingChanged || initial) renderTracking();

  if (!state.busyId && !state.trackingBusyId) {
    if (pendingExpired) {
      setActionStatus('La experiencia no llegó a iniciar. V20 volvió a Resolume.', 'error');
    } else if (pendingConfirmed) {
      const activeExperience = experiences.find(item => item.id === activeId);
      setActionStatus(activeExperience ? `${activeExperience.publicTitle} está en escena.` : 'La experiencia está en escena.', 'success');
    } else if (initial) {
      updateReadyMessage();
    } else if (tracking?.activeMode && tracking.activeMode !== previousTrackingMode) {
      const activeMode = tracking.modes.find(mode => mode.id === tracking.activeMode);
      setActionStatus(activeMode ? `${activeMode.title} está en vivo.` : 'La interacción está en vivo.', 'success');
    } else if (activeChanged && activeId) {
      const activeExperience = experiences.find(item => item.id === activeId);
      setActionStatus(activeExperience ? `${activeExperience.publicTitle} está en escena.` : 'La experiencia está en escena.', 'success');
    } else if ((activeChanged && !activeId) || (previousTrackingMode && !tracking?.activeMode)) {
      updateReadyMessage();
    }
  }
}

async function readJson(response) {
  try {
    return await response.json();
  } catch {
    return {};
  }
}

async function fetchCatalog() {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    const response = await fetch('/api/experiences', {
      cache: 'no-store',
      headers: { Accept: 'application/json' },
      signal: controller.signal
    });
    if (!response.ok) throw new Error('unavailable');
    const payload = await readJson(response);
    if (payload && payload.ok === false) throw new Error('unavailable');
    return payload;
  } finally {
    window.clearTimeout(timeout);
  }
}

async function refreshExperiences({ initial = false } = {}) {
  if (requestInFlight) return;
  requestInFlight = true;

  if (initial && !state.hasLoaded) {
    state.unavailable = false;
    elements.catalog.hidden = false;
    elements.emptyState.hidden = true;
    elements.catalog.setAttribute('aria-busy', 'true');
    setAvailability('loading', 'Preparando');
    setActionStatus('Buscando experiencias…');
  }

  try {
    const payload = await fetchCatalog();
    applyPayload(payload, initial || !state.hasLoaded);
  } catch {
    state.failures += 1;
    if (!state.hasLoaded) {
      state.unavailable = true;
      state.experiences = FALLBACK_CATALOG.map((item, index) => ({ ...item, enabled: false, visualIndex: index }));
      state.catalogSignature = experienceSignature(state.experiences);
      state.tracking = null;
      state.trackingSignature = '';
      setAvailability('unavailable', 'No disponible');
      setActionStatus('Intenta nuevamente en un momento.', 'error');
      renderCategories();
      renderCatalog();
      renderTracking();
    } else if (state.failures >= 3) {
      setAvailability('unavailable', 'Reconectando');
    }
  } finally {
    requestInFlight = false;
  }
}

function schedulePoll(delay = POLL_INTERVAL_MS) {
  window.clearTimeout(pollTimer);
  if (document.hidden) return;
  pollTimer = window.setTimeout(async () => {
    await refreshExperiences();
    schedulePoll();
  }, delay);
}

async function playExperience(item) {
  if (state.busyId || state.trackingBusyId || state.unavailable || !item.enabled) return;

  state.busyId = item.id;
  setActionStatus(`Iniciando ${item.publicTitle}…`);
  renderCatalog();
  renderTracking();

  try {
    const response = await fetch('/api/experience/play', {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ id: item.id })
    });
    const payload = await readJson(response);
    if (!response.ok || payload.ok === false) throw new Error('not-started');

    state.pendingExperienceId = item.id;
    state.pendingExperienceAt = Date.now();
    setActionStatus(`${item.publicTitle} se está preparando…`, 'success');
    elements.successTitle.textContent = `Preparando ${item.publicTitle}`;
    elements.successMessage.textContent = 'V20 recibió la solicitud. Se marcará “En escena” cuando xSchedule confirme la reproducción.';
    if (typeof elements.successDialog.showModal === 'function' && !elements.successDialog.open) {
      elements.successDialog.showModal();
    }
  } catch {
    setActionStatus('No pudimos iniciar la experiencia. Intenta nuevamente.', 'error');
  } finally {
    state.busyId = null;
    renderCatalog();
    renderTracking();
    schedulePoll(100);
  }
}

async function selectTrackingMode(mode) {
  if (!state.tracking || state.trackingBusyId || state.busyId || !state.tracking.configured || !mode.enabled) return;

  state.trackingBusyId = mode.id;
  setActionStatus(`Preparando ${mode.title}…`);
  renderCatalog();
  renderTracking();

  try {
    const response = await fetch('/api/tracking/mode', {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({ id: mode.id })
    });
    const payload = await readJson(response);
    if (!response.ok || payload.ok === false) throw new Error('not-started');

    state.pendingExperienceId = null;
    state.pendingExperienceAt = 0;
    state.tracking.desiredMode = mode.id;
    setActionStatus(`${mode.title} se está preparando.`, 'success');
  } catch {
    setActionStatus('No pudimos iniciar la interacción. Intenta nuevamente.', 'error');
  } finally {
    state.trackingBusyId = null;
    renderCatalog();
    renderTracking();
    schedulePoll(100);
  }
}

function fullscreenElement() {
  return document.fullscreenElement || document.webkitFullscreenElement || null;
}

function runsAsApp() {
  return window.matchMedia('(display-mode: fullscreen)').matches ||
    window.matchMedia('(display-mode: standalone)').matches ||
    window.navigator.standalone === true;
}

function updateFullscreenControl() {
  const active = Boolean(fullscreenElement());
  elements.fullscreenToggle.hidden = runsAsApp() && !active;
  elements.fullscreenToggle.setAttribute('aria-label', active ? 'Salir de pantalla completa' : 'Abrir en pantalla completa');
  elements.fullscreenLabel.textContent = active ? 'Salir de pantalla completa' : 'Pantalla completa';
  document.body.classList.toggle('is-fullscreen', active || runsAsApp());
}

async function enterFullscreen() {
  if (fullscreenRequestPending || fullscreenElement() || runsAsApp()) return;
  fullscreenRequestPending = true;
  try {
    const root = document.documentElement;
    if (typeof root.requestFullscreen === 'function') {
      await root.requestFullscreen();
    } else if (typeof root.webkitRequestFullscreen === 'function') {
      root.webkitRequestFullscreen();
    }
    if (window.screen?.orientation && typeof window.screen.orientation.lock === 'function') {
      try { await window.screen.orientation.lock('landscape'); } catch { }
    }
  } catch {
    // El boton permanece visible para que el usuario pueda intentarlo otra vez.
  } finally {
    fullscreenRequestPending = false;
    updateFullscreenControl();
  }
}

async function toggleFullscreen() {
  if (fullscreenElement()) {
    try {
      if (typeof document.exitFullscreen === 'function') await document.exitFullscreen();
      else if (typeof document.webkitExitFullscreen === 'function') document.webkitExitFullscreen();
      if (window.screen?.orientation && typeof window.screen.orientation.unlock === 'function') {
        window.screen.orientation.unlock();
      }
    } catch {
      // El navegador conserva el estado actual si no permite salir desde aqui.
    }
    return;
  }
  await enterFullscreen();
}

function enterFullscreenOnFirstGesture(event) {
  if (event.target instanceof Element && event.target.closest('#fullscreen-toggle')) return;
  void enterFullscreen();
}

elements.retry.addEventListener('click', async () => {
  await refreshExperiences({ initial: true });
  schedulePoll();
});
elements.fullscreenToggle.addEventListener('click', toggleFullscreen);
elements.successDialog.addEventListener('close', () => {
  if (!state.activeId) return;
  const card = [...document.querySelectorAll('[data-experience-id]')]
    .find(item => item.dataset.experienceId === state.activeId);
  card?.focus();
});

document.addEventListener('fullscreenchange', updateFullscreenControl);
document.addEventListener('webkitfullscreenchange', updateFullscreenControl);
document.addEventListener('pointerup', enterFullscreenOnFirstGesture, { capture: true, once: true });
document.addEventListener('keydown', enterFullscreenOnFirstGesture, { capture: true, once: true });
document.addEventListener('visibilitychange', () => {
  if (document.hidden) {
    window.clearTimeout(pollTimer);
    return;
  }
  void refreshExperiences().finally(() => schedulePoll());
});

updateFullscreenControl();
void refreshExperiences({ initial: true }).finally(() => schedulePoll());
