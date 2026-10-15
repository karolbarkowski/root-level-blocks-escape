// Writes the stock enclosed layouts to layouts/: node make-layouts.mjs [outDir]
// File names are <floor width>x<floor height>_<shape>_<type count>types.json.
// The floor sits on a canvas one cell larger on every side; walls are the
// non-floor cells next to floor, gates replace walls that face the outside.
import fs from 'node:fs';
import path from 'node:path';

const TYPES = ['cow', 'pig', 'cat', 'sheep', 'chicken', 'horse', 'dog'];
const TYPE_COUNTS = [4, 5, 6, 7];
const SHAPES = {
  rect: ['5x5', '5x7', '6x6', '7x8', '8x10'],
  notched: ['5x6', '6x7', '6x8', '7x9', '8x10'],
  pillars: ['5x7', '6x7', '7x7', '7x9', '8x9'],
  tworoom: ['5x7', '6x8', '7x9', '8x9', '8x10'],
  // Not in size order: each slot's position fixes its type rotation, and the
  // first slots were swapped for larger boards after smaller ones proved too tight.
  recessed: ['6x8', '7x7', '7x8', '8x9', '8x10'],
  mixed: ['6x8', '7x9', '7x8', '8x8', '8x10'],
};
// Sides whose gates sit at the end of a one-cell alcove: the main floor stops
// one cell short of the outline there and only the alcoves reach it.
const ALCOVE_SIDES = { recessed: ['top', 'right', 'bottom', 'left'], mixed: ['top', 'bottom'] };
// Fixes for layouts whose default gates can't reach the generator's default
// forced opening: another type rotation and/or a gate count change, found by
// trying alternatives against the generator.
const OVERRIDES = {
  '7x9_pillars_6types': { variant: 1, extraGates: 1 },
  '8x9_pillars_6types': { variant: 0, extraGates: -1 },
  '8x9_recessed_6types': { variant: 0, extraGates: 1 },
  '8x8_mixed_4types': { variant: 0, extraGates: 1 },
  '8x10_mixed_6types': { variant: 1, extraGates: -1 },
};
// Board size swaps for single type counts: 6x8 mixed is too tight for 7 types.
const SIZE_OVERRIDES = { '6x8_mixed_7types': '6x9' };
const SIDES = {
  top: { dx: 0, dy: -1 }, right: { dx: 1, dy: 0 },
  bottom: { dx: 0, dy: 1 }, left: { dx: -1, dy: 0 },
};

// Floor masks in floor coordinates (0..w-1, 0..h-1).
function floorMask(shape, w, h) {
  const floor = Array.from({ length: h }, () => Array(w).fill(true));
  const cut = (x, y) => { if (x >= 0 && y >= 0 && x < w && y < h) floor[y][x] = false; };
  const cutCorners = k => {
    for (let i = 0; i < k; i++) for (let j = 0; j < k; j++) {
      cut(i, j); cut(w - 1 - i, j); cut(i, h - 1 - j); cut(w - 1 - i, h - 1 - j);
    }
  };
  const centred = n => (n % 2 ? [(n - 1) / 2] : [n / 2 - 1, n / 2]);
  const alcoves = ALCOVE_SIDES[shape] ?? [];
  for (let i = 0; i < w; i++) {
    if (alcoves.includes('top')) cut(i, 0);
    if (alcoves.includes('bottom')) cut(i, h - 1);
  }
  for (let j = 0; j < h; j++) {
    if (alcoves.includes('left')) cut(0, j);
    if (alcoves.includes('right')) cut(w - 1, j);
  }
  switch (shape) {
    case 'notched': cutCorners(Math.min(w, h) >= 7 ? 2 : 1); break;
    case 'pillars': {
      const xs = w >= 7 ? [2, w - 3] : centred(w);
      const ys = h >= 7 ? [2, h - 3] : centred(h);
      if (h - 5 >= 4) ys.splice(1, 0, Math.floor((h - 1) / 2));
      for (const x of xs) for (const y of ys) cut(x, y);
      break;
    }
    case 'tworoom': {
      const waist = h >= 9 ? 2 : 1;
      const inset = w >= 7 ? 2 : 1;
      const top = Math.floor((h - waist) / 2);
      for (let y = top; y < top + waist; y++)
        for (let i = 0; i < inset; i++) { cut(i, y); cut(w - 1 - i, y); }
      break;
    }
    case 'mixed': {
      // Cut the corners of the inset room, not of the outline.
      for (const y of [1, h - 2]) { cut(0, y); cut(w - 1, y); }
      // A single centre pillar on small boards, the full centre block (up to 2x2) on large ones.
      if (w * h < 49) cut(centred(w)[0], centred(h)[0]);
      else for (const x of centred(w)) for (const y of centred(h)) cut(x, y);
      break;
    }
  }
  return floor;
}

function build(shape, w, h, typeCount, variant, extraGates = 0) {
  const W = w + 2, H = h + 2;
  const floor = new Set();
  const mask = floorMask(shape, w, h);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) if (mask[y][x]) floor.add(`${x + 1},${y + 1}`);
  const isFloor = (x, y) => floor.has(`${x},${y}`);
  const inside = (x, y) => x >= 0 && y >= 0 && x < W && y < H;
  const nextToFloor = (x, y) => Object.values(SIDES).some(d => isFloor(x + d.dx, y + d.dy));

  // Candidate gate cells: wall cells with floor inward and nothing but outside beyond.
  const runs = [];
  for (const [side, d] of Object.entries(SIDES)) {
    const cells = [];
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      if (isFloor(x, y) || !isFloor(x - d.dx, y - d.dy)) continue;
      const ox = x + d.dx, oy = y + d.dy;
      if (inside(ox, oy) && (isFloor(ox, oy) || nextToFloor(ox, oy))) continue;
      // Interior wall cells (pillars) have floor on the far side or are enclosed; skip them.
      if (!reachesOutside(ox, oy)) continue;
      cells.push(side === 'top' || side === 'bottom' ? { line: y, pos: x } : { line: x, pos: y });
    }
    cells.sort((a, b) => a.line - b.line || a.pos - b.pos);
    for (const c of cells) {
      const last = runs[runs.length - 1];
      if (last && last.side === side && last.line === c.line && last.end === c.pos - 1) last.end = c.pos;
      else runs.push({ side, line: c.line, start: c.pos, end: c.pos });
    }
  }
  function reachesOutside(x, y) {
    // Flood the non-floor, non-wall space from (x, y); true if it touches the canvas edge.
    if (!inside(x, y)) return true;
    const seen = new Set([`${x},${y}`]), queue = [[x, y]];
    while (queue.length) {
      const [cx, cy] = queue.pop();
      if (cx === 0 || cy === 0 || cx === W - 1 || cy === H - 1) return true;
      for (const d of Object.values(SIDES)) {
        const nx = cx + d.dx, ny = cy + d.dy, k = `${nx},${ny}`;
        if (seen.has(k) || isFloor(nx, ny) || nextToFloor(nx, ny)) continue;
        seen.add(k); queue.push([nx, ny]);
      }
    }
    return false;
  }

  const alcoveSides = ALCOVE_SIDES[shape] ?? [];
  const usable = runs;

  // Gate count scales with the main room's perimeter (alcoves excluded), never below the type count.
  const perimeter = 2 * (w - (alcoveSides.includes('left') + alcoveSides.includes('right')) +
    h - (alcoveSides.includes('top') + alcoveSides.includes('bottom')));
  const gateCount = Math.max(typeCount, Math.round(perimeter / 4.5) + extraGates);
  const alloc = usable.map(() => 0);
  const cap = r => Math.floor((r.end - r.start + 2) / 2);
  for (let n = 0; n < gateCount; n++) {
    // Give the next gate to the run with the most free length per gate.
    let best = -1, bestScore = -1;
    usable.forEach((r, i) => {
      if (alloc[i] >= cap(r)) return;
      const score = (r.end - r.start + 1) / (alloc[i] + 1);
      if (score > bestScore) { best = i; bestScore = score; }
    });
    if (best < 0) throw new Error(`${shape} ${w}x${h}: no room for ${gateCount} gates`);
    alloc[best]++;
  }

  const gates = [];
  usable.forEach((r, i) => {
    const n = alloc[i];
    if (!n) return;
    const len = r.end - r.start + 1;
    const glen = len >= 3 * n + 1 && len >= 5 ? 2 : 1;
    const free = len - n * glen;
    for (let k = 0; k < n; k++) {
      // Spread the gates evenly, keeping at least one wall cell between them.
      const offset = Math.round(((k + 0.5) * free) / n) + k * glen;
      gates.push({ side: r.side, line: r.line, start: r.start + offset, length: glen });
    }
  });

  // Order gates clockwise so neighbouring gates get different types.
  const cx = W / 2, cy = H / 2;
  const centre = g => {
    const mid = g.start + (g.length - 1) / 2;
    return g.side === 'top' || g.side === 'bottom' ? [mid + 0.5, g.line + 0.5] : [g.line + 0.5, mid + 0.5];
  };
  gates.sort((a, b) => {
    const [ax, ay] = centre(a), [bx, by] = centre(b);
    return Math.atan2(ay - cy, ax - cx) - Math.atan2(by - cy, bx - cx);
  });

  // Turn alcove-side gate cells into floor and move the gate one cell outward.
  for (const g of gates) {
    if (!alcoveSides.includes(g.side)) continue;
    const d = SIDES[g.side];
    for (let n = g.start; n < g.start + g.length; n++)
      floor.add(g.side === 'top' || g.side === 'bottom' ? `${n},${g.line}` : `${g.line},${n}`);
    g.line += g.side === 'top' || g.side === 'bottom' ? d.dy : d.dx;
  }

  const offset = variant % TYPES.length;
  const types = Array.from({ length: typeCount }, (_, i) => TYPES[(offset + i) % TYPES.length]);
  const counters = {};
  const out = gates.map((g, i) => {
    let type = types[i % typeCount];
    if (i === gates.length - 1 && i > 0 && i % typeCount === 0) type = types[1 % typeCount];
    counters[type] = (counters[type] || 0) + 1;
    return { id: `${type}-${counters[type]}`, side: g.side, start: g.start, length: g.length, type, line: g.line };
  });

  const gateCells = new Set();
  for (const g of out) for (let n = g.start; n < g.start + g.length; n++)
    gateCells.add(g.side === 'top' || g.side === 'bottom' ? `${n},${g.line}` : `${g.line},${n}`);
  const walls = [];
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++)
    if (!isFloor(x, y) && !gateCells.has(`${x},${y}`) && nextToFloor(x, y)) walls.push({ x, y });

  return { width: W, height: H, boundaryMode: 'enclosed', walls, gates: out };
}

// --try <name> <variant> <extraGates> writes just that layout with those settings.
const argv = process.argv.slice(2);
const trial = argv.includes('--try') ? argv.splice(argv.indexOf('--try'), 4).slice(1) : null;
const outDir = path.resolve(argv[0] ?? 'layouts');
fs.mkdirSync(outDir, { recursive: true });
let written = 0, variant = 0;
for (const [shape, sizes] of Object.entries(SHAPES)) for (const size of sizes) {
  for (const t of TYPE_COUNTS) {
    const actual = SIZE_OVERRIDES[`${size}_${shape}_${t}types`] ?? size;
    const [w, h] = actual.split('x').map(Number);
    const name = `${actual}_${shape}_${t}types`;
    let { variant: v = variant, extraGates = 0 } = OVERRIDES[name] ?? {};
    variant++;
    if (trial) {
      if (trial[0] !== name) continue;
      [v, extraGates] = [Number(trial[1]), Number(trial[2])];
    }
    const layout = build(shape, w, h, t, v, extraGates);
    fs.writeFileSync(path.join(outDir, `${name}.json`), JSON.stringify(layout, null, 2) + '\n');
    written++;
  }
}
console.log(`Wrote ${written} layouts to ${outDir}`);
