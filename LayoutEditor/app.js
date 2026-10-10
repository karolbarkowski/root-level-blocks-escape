import { SIDES, parseTypes, clone, cellKey, sizeFor, edgeCell, blank, parseLayout, validate, gateAt, paint, nextId, resize, serialize, History, enclosed, geometry, gateCells } from './model.js';

const $ = id => document.getElementById(id);
const escape = value => String(value).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const STORE = 'block-studio.layout.v2'; // v1 drafts used gate colors
let TYPES = [];
try {
  const response = await fetch('./block-types.json');
  if (!response.ok) throw new Error();
  TYPES = parseTypes(await response.text());
} catch { /* Reported once the editor is up; every gate then shows as an unknown type. */ }
const typeOf = id => TYPES.find(t => t.id === id);
const typeName = id => typeOf(id)?.name ?? `${id} (unknown)`;
const typeOptions = current => (current && !typeOf(current) ? [{ id: current, name: typeName(current) }] : []).concat(TYPES)
  .map(t => `<option value="${escape(t.id)}">${escape(t.name)}</option>`).join('');
let documentState = { name: 'untitled-layout', layout: blank() }, restored = false;
try {
  const stored = JSON.parse(localStorage.getItem(STORE));
  if (stored && typeof stored.name === 'string') { documentState = { name: stored.name, layout: parseLayout(JSON.stringify(stored.layout)).board }; restored = true; }
} catch { /* A damaged browser draft never blocks opening the editor. */ }
const history = new History(documentState);
let doc = clone(history.current), tool = 'wall', selected = -1, stroke = null, preview = null, focusCell = { x: 0, y: 0 }, toastTimer;
const board = () => doc.layout;
const svg = $('board'), C = 40, P = 36, E = 15;
const names = { wall: 'Draw walls', erase: 'Erase', gate: 'Place gates', select: 'Select' };
const hints = { wall: 'Drag across cells to paint walls. Hold Shift to erase.', erase: 'Drag to erase walls. Click a gate to remove it.', gate: 'Drag along an outer edge to set a gate’s span.', select: 'Click a gate to edit its properties.' };
const color = id => typeOf(id)?.color ?? '#c79bbc';
function toast(message) { $('toast').textContent = message; $('toast').hidden = false; clearTimeout(toastTimer); toastTimer = setTimeout(() => $('toast').hidden = true, 4200); }
function saveDraft() {
  try { localStorage.setItem(STORE, JSON.stringify(doc)); $('save-status').textContent = 'Draft saved in this browser'; }
  catch { $('save-status').textContent = 'Browser storage unavailable. Export to save.'; }
}
function commit(next = doc, message = '') {
  history.commit(next); doc = clone(history.current); saveDraft(); render(); if (message) toast(message);
}
function setTool(value) {
  tool = value;
  document.querySelectorAll('[data-tool]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.tool === value)));
  $('tool-label').textContent = names[value]; $('tool-help').textContent = value === 'gate' && enclosed(board()) ? 'Choose an exit direction, then drag a straight gate across cells. Gates replace walls.' : hints[value];
  svg.style.cursor = value === 'select' ? 'default' : 'crosshair';
}
function gateRect(gate) {
  const b = board(), horizontal = ['top', 'bottom'].includes(gate.side);
  const start = Math.max(0, gate.start), length = Math.max(0, Math.min(sizeFor(b, gate.side), gate.start + gate.length) - start);
  if (enclosed(b)) return { x: P + (horizontal ? start : gate.line ?? 0) * C, y: P + (horizontal ? gate.line ?? 0 : start) * C, width: horizontal ? length * C : C, height: horizontal ? C : length * C };
  return { x: gate.side === 'left' ? P - E : gate.side === 'right' ? P + b.width * C : P + start * C,
    y: gate.side === 'top' ? P - E : gate.side === 'bottom' ? P + b.height * C : P + start * C,
    width: horizontal ? length * C : E, height: horizontal ? E : length * C };
}
const attrs = rect => Object.entries(rect).map(([key, value]) => `${key}="${value}"`).join(' ');
function renderBoard() {
  const b = board(), walls = new Set(b.walls.map(cellKey)), shape = geometry(b);
  const gateMask = new Set(enclosed(b) ? b.gates.flatMap(g => gateCells(b, g).map(cellKey)) : []);
  svg.setAttribute('viewBox', `0 0 ${b.width * C + P * 2} ${b.height * C + P * 2}`);
  let html = enclosed(b) ? '' : `<rect x="${P - 7}" y="${P - 7}" width="${b.width * C + 14}" height="${b.height * C + 14}" rx="6" fill="#c3d0d6"/>`;
  for (let y = 0; y < b.height; y++) for (let x = 0; x < b.width; x++) {
    const kind = gateMask.has(`${x},${y}`) ? 'gate-cell' : walls.has(`${x},${y}`) ? 'wall' : shape.floor.has(`${x},${y}`) ? 'floor' : 'outside';
    html += `<rect class="board-cell board-${kind}" data-cell="${x},${y}" x="${P + x * C}" y="${P + y * C}" width="${C}" height="${C}" role="button" tabindex="${focusCell.x === x && focusCell.y === y ? 0 : -1}" aria-label="Cell ${x}, ${y}: ${kind}"/>`;
  }
  for (let x = 0; x < b.width; x++) html += `<text class="board-coordinate" text-anchor="middle" x="${P + x * C + C / 2}" y="${P - 23}">${x}</text>`;
  for (let y = 0; y < b.height; y++) html += `<text class="board-coordinate" text-anchor="end" x="${P - 23}" y="${P + y * C + C / 2 + 3}">${y}</text>`;
  if (!enclosed(b)) for (const side of SIDES) for (let index = 0; index < sizeFor(b, side); index++) {
    html += `<rect class="board-edge" ${attrs(gateRect({ side, start: index, length: 1 }))} data-side="${side}" data-index="${index}" role="button" tabindex="0" aria-label="${side} edge ${index}: place or select gate"/>`;
  }
  b.gates.forEach((g, i) => {
    const rect = gateRect(g); if (rect.width <= 0 || rect.height <= 0) return;
    html += `<rect class="board-gate ${i === selected ? 'selected' : ''}" ${attrs(rect)} rx="4" fill="${color(g.type)}" data-gate="${i}" role="button" tabindex="0" aria-label="${escape(g.id)}: ${escape(typeName(g.type))} gate, ${g.side}, start ${g.start}, length ${g.length}"><title>${escape(g.id)} · ${escape(typeName(g.type))} · ${g.side} ${g.start}–${g.start + g.length - 1}</title></rect>`;
    if (enclosed(b)) html += `<text class="gate-label gate-direction" text-anchor="middle" x="${rect.x + rect.width / 2}" y="${rect.y + rect.height / 2 + 6}">${{ top: '↑', right: '→', bottom: '↓', left: '←' }[g.side]}</text>`;
    else if (g.length >= 2 && ['top', 'bottom'].includes(g.side)) html += `<text class="gate-label" text-anchor="middle" x="${rect.x + rect.width / 2}" y="${rect.y + 11}">${escape(typeName(g.type).length > 12 ? typeName(g.type).slice(0, 11) + '…' : typeName(g.type))}</text>`;
  });
  if (preview) html += `<rect class="preview-gate" ${attrs(gateRect(preview))} rx="3"/>`;
  svg.innerHTML = html;
  fitBoard();
}
function fitBoard() {
  const zoom = $('zoom').value, b = board();
  if (zoom === 'fit') {
    const panel = $('canvas-scroll');
    const availableW = Math.max(180, panel.clientWidth - 56), availableH = Math.max(180, panel.clientHeight - 56);
    $('board-wrap').style.width = `${Math.min(availableW, availableH * (b.width * C + P * 2) / (b.height * C + P * 2))}px`;
    $('board-wrap').style.maxWidth = 'none';
  } else $('board-wrap').style.width = `${(b.width * C + P * 2) * Number(zoom) / 100}px`;
}
function render() {
  const b = board(), report = validate(b, TYPES);
  if (selected >= b.gates.length) selected = -1;
  focusCell = { x: Math.min(focusCell.x, b.width - 1), y: Math.min(focusCell.y, b.height - 1) };
  $('filename').value = doc.name; $('width').value = b.width; $('height').value = b.height;
  $('boundary-mode').value = enclosed(b) ? 'enclosed' : 'rectangle';
  $('paint-direction-field').hidden = !enclosed(b); $('gate-line-field').hidden = !enclosed(b); $('gate-line').required = enclosed(b);
  setTool(tool);
  $('board-size').textContent = `${b.width} × ${b.height}`;
  $('undo').disabled = !history.past.length; $('redo').disabled = !history.future.length;
  $('floor-count').textContent = report.floorCount; $('wall-count').textContent = new Set(b.walls.map(cellKey)).size; $('gate-count').textContent = b.gates.length;
  $('validation-badge').textContent = report.errors.length ? `${report.errors.length} errors` : report.warnings.length ? 'Check notes' : 'Ready';
  $('validation-badge').className = `badge ${report.errors.length ? 'error' : report.warnings.length ? 'warning' : ''}`;
  $('validation-messages').innerHTML = [...report.errors.map(e => `<p class="validation-message error">${escape(e)}</p>`), ...report.warnings.map(w => `<p class="validation-message warning">${escape(w)}</p>`)].join('') || '<p class="validation-message">Walls and gates are compatible with the generator.</p>';
  $('export-button').disabled = report.errors.length > 0;
  $('export-button').title = report.errors.length ? 'Fix the layout errors before exporting' : 'Download layout JSON (Ctrl+S)';
  $('gate-list').innerHTML = b.gates.length ? b.gates.map((g, i) => `<button class="gate-row ${selected === i ? 'selected' : ''}" data-gate-row="${i}" aria-label="Edit ${escape(g.id)}"><span class="color-dot" style="--gate-color:${color(g.type)}"></span><span class="gate-description"><strong>${escape(g.id || 'Unnamed gate')}</strong><small>${escape(typeName(g.type))} · ${g.side} · ${g.length} ${g.length === 1 ? 'cell' : 'cells'}</small></span><span class="arrow" aria-hidden="true">›</span></button>`).join('') : '<div class="empty-gates"><strong>No gates yet</strong>Choose Place gates, then draw a span in the boundary.</div>';
  $('gate-inspector').hidden = selected < 0;
  if (selected >= 0) {
    const g = b.gates[selected];
    $('gate-type').innerHTML = typeOptions(g.type);
    for (const key of ['id', 'side', 'type', 'start', 'length']) $(`gate-${key}`).value = g[key];
    $('gate-line').value = g.line ?? 0;
  }
  renderBoard();
}
function selectGate(index) { selected = index; render(); }
function hit(event) {
  const point = svg.createSVGPoint(); point.x = event.clientX; point.y = event.clientY;
  const matrix = svg.getScreenCTM(); if (!matrix) return null;
  const p = point.matrixTransform(matrix.inverse()), b = board();
  const x = Math.floor((p.x - P) / C), y = Math.floor((p.y - P) / C);
  if (x >= 0 && y >= 0 && x < b.width && y < b.height) return { kind: 'cell', x, y };
  if (enclosed(b)) return null;
  if (x >= 0 && x < b.width && p.y >= P - E && p.y < P) return { kind: 'edge', side: 'top', index: x };
  if (x >= 0 && x < b.width && p.y >= P + b.height * C && p.y <= P + b.height * C + E) return { kind: 'edge', side: 'bottom', index: x };
  if (y >= 0 && y < b.height && p.x >= P - E && p.x < P) return { kind: 'edge', side: 'left', index: y };
  if (y >= 0 && y < b.height && p.x >= P + b.width * C && p.x <= P + b.width * C + E) return { kind: 'edge', side: 'right', index: y };
  return null;
}
function paintTo(cell) {
  // Interpolate skipped cells during fast pointer movement.
  const from = stroke.last || cell, steps = Math.max(Math.abs(cell.x - from.x), Math.abs(cell.y - from.y));
  for (let i = 0; i <= steps; i++) {
    const t = steps ? i / steps : 0;
    paint(board(), { x: Math.round(from.x + (cell.x - from.x) * t), y: Math.round(from.y + (cell.y - from.y) * t) }, stroke.erase);
  }
  stroke.last = cell; renderBoard();
}
function placeGate(side, start, length = 1, line) {
  const chosen = $('paint-type').value;
  if (!chosen) { toast('Choose a block type first.'); return; }
  const next = clone(doc), g = { id: nextId(board()), side, start, length, type: chosen };
  if (enclosed(board())) {
    g.line = line;
    const cells = new Set(gateCells(board(), g).map(cellKey));
    if (board().gates.some(other => gateCells(board(), other).some(c => cells.has(cellKey(c))))) { toast('This span overlaps another gate.'); return; }
    next.layout.walls = next.layout.walls.filter(c => !cells.has(cellKey(c)));
  }
  next.layout.gates.push(g);
  const before = validate(board()).errors, after = validate(next.layout).errors;
  const added = after.filter(error => !before.includes(error));
  if (added.length && !enclosed(board())) { toast(added[0]); return; }
  selected = next.layout.gates.length - 1; commit(next, `Added ${typeName(chosen)} gate · ${length} ${length === 1 ? 'cell' : 'cells'}`);
}
svg.addEventListener('pointerdown', event => {
  if (event.button !== 0 && event.button !== 2) return;
  const target = hit(event); if (!target) return;
  event.preventDefault();
  if (target.kind === 'edge') {
    const index = gateAt(board(), target.side, target.index);
    if (index >= 0) { if (tool === 'erase' || event.button === 2) { selected = index; deleteGate(); } else selectGate(index); return; }
    if (tool !== 'gate') { toast('Use Place gates to draw along this edge.'); return; }
    stroke = { kind: 'gate', side: target.side, start: target.index }; preview = { side: target.side, start: target.index, length: 1 };
  } else {
    focusCell = { x: target.x, y: target.y };
    if (enclosed(board())) {
      const index = board().gates.findIndex(g => gateCells(board(), g).some(c => c.x === target.x && c.y === target.y));
      if (index >= 0) { selected = index; if (tool === 'erase' || event.button === 2 || event.shiftKey) deleteGate(); else selectGate(index); return; }
      if (tool === 'gate' && event.button === 0 && !event.shiftKey) {
        const side = $('paint-direction').value, horizontal = ['top', 'bottom'].includes(side);
        stroke = { kind: 'gate', side, start: horizontal ? target.x : target.y, line: horizontal ? target.y : target.x };
        preview = { ...stroke, length: 1 }; svg.setPointerCapture(event.pointerId); renderBoard(); return;
      }
    }
    if (tool !== 'wall' && tool !== 'erase' && event.button !== 2) return;
    stroke = { kind: 'paint', erase: tool === 'erase' || event.shiftKey || event.button === 2 };
    const before = board().walls.length; paintTo(target);
    if (!stroke.erase && board().walls.length === before && !board().walls.some(c => c.x === target.x && c.y === target.y)) toast('A gate needs this cell clear. Erase or move the gate first.');
  }
  svg.setPointerCapture(event.pointerId); renderBoard();
});
svg.addEventListener('pointermove', event => {
  const target = hit(event);
  if (target?.kind === 'cell') $('cursor-position').textContent = `${target.x} · ${target.y}`;
  else if (target?.kind === 'edge') $('cursor-position').textContent = `${target.side} ${target.index}`;
  if (!stroke || !target) return;
  if (stroke.kind === 'gate' && target.kind === 'cell' && enclosed(board())) {
    const index = ['top', 'bottom'].includes(stroke.side) ? target.x : target.y;
    preview = { side: stroke.side, line: stroke.line, start: Math.min(stroke.start, index), length: Math.abs(stroke.start - index) + 1 }; renderBoard();
  }
  if (stroke.kind === 'paint' && target.kind === 'cell') paintTo(target);
  if (stroke.kind === 'gate' && target.kind === 'edge' && target.side === stroke.side) {
    preview = { side: stroke.side, start: Math.min(stroke.start, target.index), length: Math.abs(stroke.start - target.index) + 1 }; renderBoard();
  }
});
svg.addEventListener('pointerup', () => {
  if (!stroke) return;
  const gate = preview, kind = stroke.kind; stroke = null; preview = null;
  if (kind === 'gate' && gate) placeGate(gate.side, gate.start, gate.length, gate.line); else commit();
  renderBoard();
});
function cancelStroke() { if (stroke) { doc = clone(history.current); stroke = null; preview = null; render(); } }
svg.addEventListener('pointercancel', cancelStroke);
svg.addEventListener('contextmenu', event => event.preventDefault());
svg.addEventListener('keydown', event => {
  const cell = event.target.dataset.cell;
  if (cell) {
    const [x, y] = cell.split(',').map(Number); focusCell = { x, y };
    const moves = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
    if (moves[event.key]) {
      event.preventDefault(); const [dx, dy] = moves[event.key]; focusCell = { x: Math.max(0, Math.min(board().width - 1, x + dx)), y: Math.max(0, Math.min(board().height - 1, y + dy)) };
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      const gateIndex = enclosed(board()) ? board().gates.findIndex(g => gateCells(board(), g).some(c => c.x === x && c.y === y)) : -1;
      if (gateIndex >= 0) { selectGate(gateIndex); if (tool === 'erase' || event.shiftKey) deleteGate(); }
      else if (tool === 'wall' || tool === 'erase') { paint(board(), focusCell, tool === 'erase' || event.shiftKey); commit(); }
      else if (tool === 'gate' && enclosed(board())) { const side = $('paint-direction').value, horizontal = ['top', 'bottom'].includes(side); placeGate(side, horizontal ? x : y, 1, horizontal ? y : x); }
    } else return;
    renderBoard(); svg.querySelector(`[data-cell="${focusCell.x},${focusCell.y}"]`)?.focus();
  } else if (event.key === 'Enter' || event.key === ' ') {
    event.preventDefault();
    const index = event.target.dataset.gate;
    if (index !== undefined) { selectGate(Number(index)); if (tool === 'erase') deleteGate(); }
    else if (event.target.dataset.side) { const side = event.target.dataset.side, pos = Number(event.target.dataset.index), g = gateAt(board(), side, pos); if (g >= 0) selectGate(g); else placeGate(side, pos); }
  }
});
document.querySelectorAll('[data-tool]').forEach(button => button.addEventListener('click', () => setTool(button.dataset.tool)));
$('swatches').innerHTML = TYPES.map(t => `<button class="swatch" data-type="${escape(t.id)}" style="--swatch:${t.color}" aria-label="${escape(t.name)}" title="${escape(t.name)}"></button>`).join('');
$('paint-type').innerHTML = typeOptions();
function updateSwatches() { document.querySelectorAll('[data-type]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.type === $('paint-type').value))); }
$('swatches').addEventListener('click', event => { const button = event.target.closest('[data-type]'); if (button) { $('paint-type').value = button.dataset.type; updateSwatches(); setTool('gate'); } });
$('paint-type').addEventListener('change', updateSwatches);
updateSwatches();
$('filename').addEventListener('change', () => { doc.name = $('filename').value.trim().replace(/\.json$/i, '') || 'untitled-layout'; commit(); });
$('zoom').addEventListener('change', fitBoard); new ResizeObserver(fitBoard).observe($('canvas-scroll'));
$('resize-form').addEventListener('submit', event => {
  event.preventDefault();
  try {
    const next = resize(board(), Number($('width').value), Number($('height').value));
    const walls = board().walls.length - next.walls.length, gates = board().gates.length - next.gates.length;
    if ((walls || gates) && !confirm(`Resizing will remove ${walls} wall cells and ${gates} gates outside the new bounds. Resize? You can undo this.`)) return;
    selected = -1; commit({ ...doc, layout: next }, `Board resized to ${next.width} × ${next.height}`);
  } catch (error) { toast(error.message); }
});
function navigateHistory(direction) { cancelStroke(); if (history[direction]()) { doc = clone(history.current); selected = -1; saveDraft(); render(); } }
$('undo').onclick = () => navigateHistory('undo'); $('redo').onclick = () => navigateHistory('redo');
$('new-button').onclick = () => $('new-dialog').showModal();
$('new-form').onsubmit = event => {
  event.preventDefault(); const width = Number($('new-width').value), height = Number($('new-height').value);
  try { const next = resize(blank(), width, height); selected = -1; commit({ name: $('new-name').value.trim() || 'untitled-layout', layout: next }, 'Created a new layout'); $('new-dialog').close(); }
  catch (error) { toast(error.message); }
};
document.querySelectorAll('[data-close]').forEach(button => button.onclick = () => $(button.dataset.close).close());
function importText(text, name = doc.name) {
  const imported = parseLayout(text); selected = -1;
  commit({ name: name.replace(/\.json$/i, ''), layout: imported.board }, imported.extracted ? 'Imported board geometry. Blocks and solutions were not included.' : 'Layout imported');
}
async function importFile(file) {
  if (!file) return;
  try { if (file.size > 1024 * 1024) throw new Error('Please choose a JSON file smaller than 1 MB.'); importText(await file.text(), file.name); }
  catch (error) { toast(error.message); }
}
$('open-button').onclick = () => $('file-input').click();
$('file-input').onchange = async () => { await importFile($('file-input').files[0]); $('file-input').value = ''; };
let dragDepth = 0;
window.addEventListener('dragenter', event => { if (Array.from(event.dataTransfer?.types || []).includes('Files')) { event.preventDefault(); dragDepth++; $('drop-overlay').hidden = false; } });
window.addEventListener('dragover', event => { if (Array.from(event.dataTransfer?.types || []).includes('Files')) event.preventDefault(); });
window.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; $('drop-overlay').hidden = true; } });
window.addEventListener('drop', event => { event.preventDefault(); dragDepth = 0; $('drop-overlay').hidden = true; if (event.dataTransfer?.files.length) importFile(event.dataTransfer.files[0]); });
$('example-button').onclick = async () => {
  try { const response = await fetch('./examples/enclosed-layout.json'); if (!response.ok) throw new Error('Example could not be loaded.'); importText(await response.text(), 'enclosed-layout'); }
  catch (error) { toast(error.message); }
};
$('boundary-mode').onchange = () => {
  const next = clone(doc), mode = $('boundary-mode').value;
  next.layout.boundaryMode = mode;
  next.layout.gates = next.layout.gates.map(g => {
    const converted = { ...g };
    if (mode === 'enclosed') converted.line = g.side === 'left' || g.side === 'top' ? 0 : g.side === 'right' ? board().width - 1 : board().height - 1;
    else delete converted.line;
    return converted;
  });
  selected = -1; commit(next, 'Boundary mode changed. Check gate positions and close the boundary. Undo restores the previous mode.');
};
function exportFile() {
  try {
    const text = serialize(board(), TYPES);
    const safe = (doc.name.replace(/[<>:"/\\|?*\u0000-\u001f]/g, '-').replace(/[. ]+$/, '').replace(/\.json$/i, '') || 'layout') + '.json';
    const url = URL.createObjectURL(new Blob([text], { type: 'application/json' }));
    const link = document.createElement('a'); link.href = url; link.download = safe; document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 15000); toast(`Exported ${safe}`);
  } catch (error) { toast(error.message); }
}
$('export-button').onclick = exportFile;
$('gate-list').onclick = event => { const button = event.target.closest('[data-gate-row]'); if (button) selectGate(Number(button.dataset.gateRow)); };
$('add-gate').onclick = () => { setTool('gate'); toast(enclosed(board()) ? 'Choose an exit direction, then drag across wall or empty cells.' : 'Drag along an outer edge.'); };
$('gate-form').onsubmit = event => {
  event.preventDefault(); if (selected < 0) return;
  doc.layout.gates[selected] = { id: $('gate-id').value.trim(), side: $('gate-side').value, start: Number($('gate-start').value), length: Number($('gate-length').value), type: $('gate-type').value };
  if (enclosed(board())) {
    doc.layout.gates[selected].line = Number($('gate-line').value);
    const cells = new Set(gateCells(board(), doc.layout.gates[selected]).map(cellKey));
    doc.layout.walls = doc.layout.walls.filter(c => !cells.has(cellKey(c)));
  }
  commit();
};
function deleteGate() { if (selected < 0) return; doc.layout.gates.splice(selected, 1); selected = -1; commit(doc, 'Gate deleted'); }
$('delete-gate').onclick = deleteGate; $('deselect').onclick = () => selectGate(-1);
$('json-button').onclick = () => { $('json-text').value = JSON.stringify(board(), null, 2); $('json-error').textContent = ''; $('json-dialog').showModal(); };
$('apply-json').onclick = () => { try { importText($('json-text').value); $('json-dialog').close(); } catch (error) { $('json-error').textContent = error.message; } };
$('copy-json').onclick = async () => {
  try { await navigator.clipboard.writeText($('json-text').value); toast('JSON copied'); }
  catch { $('json-text').select(); $('json-error').textContent = 'Clipboard access is unavailable. Press Ctrl+C to copy the selected JSON.'; }
};
window.addEventListener('keydown', event => {
  const input = /INPUT|SELECT|TEXTAREA/.test(event.target.tagName), dialog = document.querySelector('dialog[open]');
  if (dialog) return;
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') { event.preventDefault(); exportFile(); }
  else if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'o') { event.preventDefault(); $('file-input').click(); }
  else if (!input && (event.ctrlKey || event.metaKey) && ['z', 'y'].includes(event.key.toLowerCase())) { event.preventDefault(); navigateHistory(event.shiftKey || event.key.toLowerCase() === 'y' ? 'redo' : 'undo'); }
  else if (!input && !event.ctrlKey && !event.metaKey && ['1', '2', '3', '4'].includes(event.key)) setTool(['wall', 'erase', 'gate', 'select'][Number(event.key) - 1]);
  else if (!input && event.key === 'Delete') deleteGate();
  else if (event.key === 'Escape') { cancelStroke(); selectGate(-1); }
});
render(); setTool('wall');
if (!TYPES.length) toast('Block types could not be loaded. Start the editor with start.ps1 so /block-types.json is served.');
else if (restored) toast('Restored your last browser draft'); else saveDraft();

