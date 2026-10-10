export const SIDES = ['top', 'right', 'bottom', 'left'];
// Block types come from Shared/BlockTypes/block-types.json, which the server publishes as /block-types.json.
export function parseTypes(text) {
  const file = JSON.parse(text);
  if (file?.format !== 'block-types/v1' || !Array.isArray(file.types) || !file.types.length) throw new Error('block-types.json is missing or has an unknown format.');
  const ids = new Set();
  for (const t of file.types) {
    if (typeof t?.id !== 'string' || !t.id.trim() || ids.has(t.id) || typeof t.name !== 'string' || !/^#[0-9a-f]{6}$/i.test(t.color))
      throw new Error('Every block type needs a unique id, a name and a #RRGGBB color.');
    ids.add(t.id);
  }
  return file.types.map(({ id, name, color }) => ({ id, name, color }));
}
export const clone = value => structuredClone(value);
export const cellKey = ({ x, y }) => `${x},${y}`;
export const sizeFor = (board, side) => ['top', 'bottom'].includes(side) ? board.width : board.height;
export function edgeCell(board, side, index) {
  return side === 'top' ? { x: index, y: 0 } : side === 'bottom' ? { x: index, y: board.height - 1 }
    : side === 'left' ? { x: 0, y: index } : { x: board.width - 1, y: index };
}
export const enclosed = board => board.boundaryMode === 'enclosed';
export function blank(width = 10, height = 10) { return { width, height, boundaryMode: 'enclosed', walls: [], gates: [] }; }
export function gateCells(board, gate) {
  return Array.from({ length: Math.min(20, Math.max(0, gate.length)) }, (_, i) => {
    const c = edgeCell(board, gate.side, gate.start + i);
    if (enclosed(board)) { if (['top', 'bottom'].includes(gate.side)) c.y = gate.line; else c.x = gate.line; }
    return c;
  });
}
export function geometry(board) {
  const blocked = new Set(board.walls.map(cellKey)), exterior = new Set(), floor = new Set();
  if (enclosed(board)) board.gates.forEach(g => gateCells(board, g).forEach(c => blocked.add(cellKey(c))));
  const inside = (x, y) => x >= 0 && y >= 0 && x < board.width && y < board.height;
  const queue = [];
  function visit(x, y) { const key = `${x},${y}`; if (inside(x, y) && !blocked.has(key) && !exterior.has(key)) { exterior.add(key); queue.push({ x, y }); } }
  if (enclosed(board)) {
    for (let x = 0; x < board.width; x++) { visit(x, 0); visit(x, board.height - 1); }
    for (let y = 0; y < board.height; y++) { visit(0, y); visit(board.width - 1, y); }
    for (let i = 0; i < queue.length; i++) { const { x, y } = queue[i]; visit(x - 1, y); visit(x + 1, y); visit(x, y - 1); visit(x, y + 1); }
  }
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const key = `${x},${y}`; if (!blocked.has(key) && !exterior.has(key)) floor.add(key);
  }
  return { floor, exterior, blocked };
}
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);
function fields(value, keys, label, optional = []) {
  if (!record(value)) throw new Error(`${label} must be an object.`);
  if (Object.keys(value).some(key => !keys.includes(key) && !optional.includes(key))) throw new Error(`${label} contains unsupported fields.`);
  if (keys.some(key => !(key in value))) throw new Error(`${label} is missing a required field.`);
}
// Structural failures are rejected. Repairable geometric conflicts are retained
// and surfaced by validate(), so an imported layout never silently loses data.
export function parseLayout(text) {
  let input;
  try { input = JSON.parse(text); } catch { throw new Error('This is not valid JSON. Check commas, quotes, and brackets.'); }
  const extracted = record(input) && 'board' in input;
  const board = extracted ? input.board : input;
  fields(board, ['width', 'height', 'walls', 'gates'], 'Layout', ['boundaryMode']);
  if ('boundaryMode' in board && !['rectangle', 'enclosed'].includes(board.boundaryMode)) throw new Error('Unknown boundary mode.');
  if (![board.width, board.height].every(n => Number.isInteger(n) && n >= 2 && n <= 20)) throw new Error('Width and height must be whole numbers from 2 to 20.');
  if (!Array.isArray(board.walls) || !Array.isArray(board.gates)) throw new Error('Walls and gates must be arrays.');
  if (board.walls.length > 4000 || board.gates.length > 1000) throw new Error('This layout contains too many entries.');
  board.walls.forEach((wall, i) => {
    fields(wall, ['x', 'y'], `Wall ${i + 1}`);
    if (![wall.x, wall.y].every(Number.isInteger) || wall.x < 0 || wall.y < 0 || wall.x >= board.width || wall.y >= board.height)
      throw new Error(`Wall ${i + 1} is outside the board or has invalid coordinates.`);
  });
  board.gates.forEach((gate, i) => {
    if ('color' in gate && !('type' in gate)) throw new Error('This layout uses colors from the old format. Convert it with “LevelGenerator.Tools migrate” first.');
    fields(gate, ['id', 'side', 'start', 'length', 'type'], `Gate ${i + 1}`, ['line']);
    if ('line' in gate && !Number.isInteger(gate.line)) throw new Error(`Gate ${i + 1} needs an integer line.`);
    if (typeof gate.id !== 'string' || typeof gate.type !== 'string' || !SIDES.includes(gate.side) ||
        !Number.isInteger(gate.start) || !Number.isInteger(gate.length)) throw new Error(`Gate ${i + 1} has invalid field types or an unknown side.`);
  });
  return { board: clone(board), extracted };
}
// Pass the block types to check gate types against them; without them only empty types are reported.
export function validate(board, types = null) {
  const known = types && new Set(types.map(t => t.id));
  const shape = geometry(board);
  const errors = [], warnings = [], walls = new Set(), ids = new Set(), edges = new Map();
  for (const wall of board.walls) {
    const key = cellKey(wall);
    if (walls.has(key)) errors.push(`Duplicate wall at (${wall.x}, ${wall.y}). Erase and repaint this cell.`);
    walls.add(key);
  }
  board.gates.forEach((gate, index) => {
    const name = gate.id || `Gate ${index + 1}`;
    if (!gate.id.trim()) errors.push(`Gate ${index + 1} needs an ID.`);
    if (ids.has(gate.id)) errors.push(`Gate ID “${gate.id}” is used more than once.`);
    ids.add(gate.id);
    if (!gate.type.trim()) errors.push(`${name} needs a block type.`);
    else if (known && !known.has(gate.type)) errors.push(`${name} uses unknown block type “${gate.type}”.`);
    const size = sizeFor(board, gate.side);
    if (enclosed(board) ? !Number.isInteger(gate.line) || gate.line < 0 || gate.line >= (['top', 'bottom'].includes(gate.side) ? board.height : board.width) : 'line' in gate) {
      errors.push(`${name}: enclosed gates need an in-bounds line; rectangular gates must omit it.`); return;
    }
    if (gate.start < 0 || gate.length < 1 || gate.start + gate.length > size) {
      errors.push(`${name} must fit within the ${gate.side} edge (0–${size - 1}).`); return;
    }
    for (let j = gate.start; j < gate.start + gate.length; j++) {
      const c = gateCells(board, gate)[j - gate.start];
      const key = enclosed(board) ? cellKey(c) : `${gate.side}:${j}`;
      if (edges.has(key)) errors.push(`${name} overlaps ${edges.get(key)} on the ${gate.side} edge.`);
      edges.set(key, name);
      if (walls.has(cellKey(c))) errors.push(`${name} overlaps or faces a wall at position ${j}.`);
      if (enclosed(board)) {
        const [dx, dy] = { top: [0, -1], right: [1, 0], bottom: [0, 1], left: [-1, 0] }[gate.side];
        const ix = c.x - dx, iy = c.y - dy, ox = c.x + dx, oy = c.y + dy;
        if (!shape.floor.has(`${ix},${iy}`) || (ox >= 0 && oy >= 0 && ox < board.width && oy < board.height && !shape.exterior.has(`${ox},${oy}`)))
          errors.push(`${name} must face enclosed floor inward and outside space outward. Check its exit direction.`);
      }
    }
  });
  if (board.gates.length === 0) warnings.push('Add a gate before generating blocks.');
  const floorCount = shape.floor.size;
  if (enclosed(board) && !floorCount) errors.push('No enclosed floor yet. Close the boundary with walls and gates.');
  if (floorCount === 0) warnings.push('There are no free cells for blocks.');
  let components = 0;
  const seen = new Set([...shape.blocked, ...shape.exterior]);
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    if (seen.has(`${x},${y}`)) continue;
    components++;
    const queue = [{ x, y }]; seen.add(`${x},${y}`);
    for (let i = 0; i < queue.length; i++) {
      const c = queue[i];
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = c.x + dx, ny = c.y + dy, key = `${nx},${ny}`;
        if (nx >= 0 && ny >= 0 && nx < board.width && ny < board.height && !seen.has(key)) { seen.add(key); queue.push({ x: nx, y: ny }); }
      }
    }
  }
  if (components > 1) warnings.push(`The floor has ${components} disconnected regions. This can restrict generation.`);
  return { errors: [...new Set(errors)], warnings, floorCount, components };
}
export function gateAt(board, side, index) { return board.gates.findIndex(g => g.side === side && index >= g.start && index < g.start + g.length); }
export function wallAllowed(board, cell) {
  return !board.gates.some(g => gateCells(board, g).some(c => cellKey(c) === cellKey(cell)));
}
export function paint(board, cell, erase = false) {
  if (cell.x < 0 || cell.y < 0 || cell.x >= board.width || cell.y >= board.height) return false;
  const key = cellKey(cell), exists = board.walls.some(w => cellKey(w) === key);
  if (erase) { board.walls = board.walls.filter(w => cellKey(w) !== key); return exists; }
  if (exists || !wallAllowed(board, cell)) return false;
  board.walls.push({ ...cell }); return true;
}
export function nextId(board) { let n = 1; while (board.gates.some(g => g.id === `gate-${n}`)) n++; return `gate-${n}`; }
export function resize(board, width, height) {
  if (![width, height].every(n => Number.isInteger(n) && n >= 2 && n <= 20)) throw new Error('Use whole-number dimensions between 2 and 20.');
  return { ...board, width, height, walls: board.walls.filter(c => c.x < width && c.y < height),
    gates: board.gates.filter(g => g.start >= 0 && g.length > 0 && g.start + g.length <= sizeFor({ width, height }, g.side) &&
      (!enclosed(board) || g.line < (['top', 'bottom'].includes(g.side) ? height : width))) };
}
export function serialize(board, types = null) {
  const report = validate(board, types);
  if (report.errors.length) throw new Error('Fix the layout errors before exporting.');
  return JSON.stringify({ width: board.width, height: board.height,
    ...(board.boundaryMode ? { boundaryMode: board.boundaryMode } : {}),
    walls: [...board.walls].sort((a, b) => a.y - b.y || a.x - b.x),
    gates: board.gates.map(({ id, side, start, length, type, line }) => ({ id, side, start, length, type, ...(line !== undefined ? { line } : {}) })) }, null, 2) + '\n';
}
export class History {
  constructor(initial) { this.current = clone(initial); this.past = []; this.future = []; }
  commit(next) {
    if (JSON.stringify(next) === JSON.stringify(this.current)) return false;
    this.past.push(clone(this.current)); if (this.past.length > 100) this.past.shift();
    this.current = clone(next); this.future = []; return true;
  }
  undo() { if (!this.past.length) return false; this.future.push(this.current); this.current = this.past.pop(); return true; }
  redo() { if (!this.future.length) return false; this.past.push(this.current); this.current = this.future.pop(); return true; }
}
