'use strict';

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
  category: 'Todas',
  busyId: null,
  activeId: null,
  unavailable: false
};

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
  card.disabled = state.unavailable || state.busyId !== null || !item.enabled;
  card.classList.toggle('is-unavailable', !item.enabled || state.unavailable);
  card.classList.toggle('is-busy', state.busyId === item.id);
  card.classList.toggle('is-active', state.activeId === item.id);
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

async function readJson(response) {
  try {
    return await response.json();
  } catch {
    return {};
  }
}

async function loadExperiences() {
  state.unavailable = false;
  elements.catalog.hidden = false;
  elements.emptyState.hidden = true;
  elements.catalog.setAttribute('aria-busy', 'true');
  setAvailability('loading', 'Preparando');
  setActionStatus('Buscando experiencias…');

  try {
    const response = await fetch('/api/experiences', {
      cache: 'no-store',
      headers: { Accept: 'application/json' }
    });
    if (!response.ok) throw new Error('unavailable');

    const payload = await readJson(response);
    if (payload && payload.ok === false) throw new Error('unavailable');
    const source = Array.isArray(payload) ? payload : payload.experiences;
    if (!Array.isArray(source)) throw new Error('invalid');

    state.experiences = source.map(normaliseExperience).filter(Boolean);
    const reportedActiveId = Object.prototype.hasOwnProperty.call(payload, 'activeExperienceId')
      ? payload.activeExperienceId
      : payload.activeId;
    state.activeId = cleanText(reportedActiveId) || null;
    state.category = 'Todas';
    setAvailability('ready', 'Listo para elegir');
    const readyCount = state.experiences.filter(item => item.enabled).length;
    const upcomingCount = state.experiences.length - readyCount;
    const readyLabel = `${readyCount} ${readyCount === 1 ? 'experiencia lista' : 'experiencias listas'}`;
    setActionStatus(upcomingCount > 0 ? `${readyLabel} · ${upcomingCount} próximamente` : readyLabel);
  } catch {
    state.unavailable = true;
    state.experiences = FALLBACK_CATALOG.map((item, index) => ({ ...item, enabled: false, visualIndex: index }));
    setAvailability('unavailable', 'No disponible');
    setActionStatus('Intenta nuevamente en un momento.', 'error');
  }

  renderCategories();
  renderCatalog();
}

async function playExperience(item) {
  if (state.busyId || state.unavailable || !item.enabled) return;

  state.busyId = item.id;
  setActionStatus(`Iniciando ${item.publicTitle}…`);
  renderCatalog();

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

    state.activeId = item.id;
    setActionStatus(`${item.publicTitle} está en escena.`, 'success');
    elements.successTitle.textContent = item.publicTitle;
    elements.successMessage.textContent = 'La experiencia ya comenzó. Disfruta el momento.';
    if (typeof elements.successDialog.showModal === 'function') {
      elements.successDialog.showModal();
    }
  } catch {
    setActionStatus('No pudimos iniciar la experiencia. Intenta nuevamente.', 'error');
  } finally {
    state.busyId = null;
    renderCatalog();
  }
}

elements.retry.addEventListener('click', loadExperiences);
elements.successDialog.addEventListener('close', () => {
  document.querySelector(`[data-experience-id="${CSS.escape(state.activeId)}"]`)?.focus();
});

loadExperiences();
