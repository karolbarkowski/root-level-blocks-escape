import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { blank as emptyCanvas, parseTypes, parseLayout, validate, paint, resize, serialize, History, gateAt, edgeCell, nextId, geometry, gateCells } from '../model.js';

const blank = (width = 6, height = 6) => ({ width, height, walls: [], gates: [] });
const types = parseTypes(await readFile(new URL('../../Shared/BlockTypes/block-types.json', import.meta.url), 'utf8'));
const gate = (values = {}) => ({ id: 'gate-1', side: 'top', start: 0, length: 2, type: types[0].id, ...values });
const enclosedExample = () => readFile(new URL('../examples/enclosed-layout.json', import.meta.url), 'utf8').then(text => parseLayout(text).board);
test('new canvas has no implicit playable rectangle', () => {
  const b = emptyCanvas(); assert.equal(b.boundaryMode, 'enclosed');
  assert.equal(validate(b).floorCount, 0); assert.throws(() => serialize(b));
});
test('sketch enclosure matches its browser export and round trips without losing gate positions', async () => {
  const b = await enclosedExample();
  assert.deepEqual(b, JSON.parse(await readFile(new URL('../verification/enclosed-export.json', import.meta.url), 'utf8')));
  assert.deepEqual(validate(b).errors, []); assert.equal(validate(b).floorCount, 44); assert.equal(validate(b).components, 1);
  assert.equal(geometry(b).exterior.size, 20); assert.deepEqual(JSON.parse(serialize(b)), b);
  assert.deepEqual(gateCells(b, b.gates[0]), [{ x: 2, y: 6 }, { x: 2, y: 7 }, { x: 2, y: 8 }]);
  assert.equal(paint(b, { x: 2, y: 7 }), false);
});
test('opening the boundary removes floor and closing it restores the enclosed region', async () => {
  const b = await enclosedExample(); paint(b, { x: 4, y: 0 }, true);
  assert.equal(validate(b).floorCount, 0); assert.throws(() => serialize(b));
  paint(b, { x: 4, y: 0 }); assert.equal(validate(b).floorCount, 44);
  b.gates.pop(); assert.equal(validate(b).floorCount, 0);
});
test('inverted, overlapping, missing-line and out-of-range inset gates block export', async () => {
  for (const edit of [b => b.gates[0].side = 'right', b => b.gates.push({ ...b.gates[0], id: 'another' }),
    b => delete b.gates[0].line, b => b.gates[0].line = 10, b => b.walls.push({ x: 2, y: 7 })]) {
    const b = await enclosedExample(); edit(b); assert.throws(() => serialize(b));
  }
});
test('resizing an enclosure keeps inset gate coordinates and crops whole gates', async () => {
  const b = await enclosedExample(); const larger = resize(b, 20, 20);
  assert.equal(larger.boundaryMode, 'enclosed'); assert.deepEqual(larger.gates, b.gates); assert.equal(validate(larger).floorCount, 44);
  assert.equal(resize(b, 6, 10).gates.length, 1);
});
test('round-trips a legacy rectangular layout without extra metadata', async () => {
  const text = await readFile(new URL('../verification/exported-layout.json', import.meta.url), 'utf8');
  const { board, extracted } = parseLayout(text);
  assert.equal(extracted, false); assert.deepEqual(validate(board).errors, []);
  assert.deepEqual(JSON.parse(serialize(board)), JSON.parse(text));
});
test('extracts only board geometry from an existing generated level', async () => {
  const text = await readFile(new URL('../verification/generated/level-001.json', import.meta.url), 'utf8');
  const result = parseLayout(text); assert.equal(result.extracted, true);
  assert.deepEqual(Object.keys(JSON.parse(serialize(result.board))), ['width', 'height', 'walls', 'gates']);
});
test('rejects malformed, missing, unsupported, or noninteger data', () => {
  for (const value of [null, [], {}, { ...blank(), width: 21 }, { ...blank(), height: 2.5 }, { ...blank(), metadata: 1 },
    { ...blank(), walls: [{ x: 6, y: 0 }] }, { ...blank(), gates: [gate({ side: 'center' })] }, { ...blank(), gates: [gate({ length: '3' })] }])
    assert.throws(() => parseLayout(JSON.stringify(value)));
  assert.throws(() => parseLayout('{oops'));
});
test('accepts 2x2 and 20x20 boundaries', () => {
  assert.equal(parseLayout(JSON.stringify(blank(20, 20))).board.width, 20);
  assert.equal(validate(blank(2, 2)).floorCount, 4);
});
test('painting is idempotent and cannot block a gate approach', () => {
  const b = blank(); b.gates.push(gate());
  assert.equal(paint(b, { x: 0, y: 0 }), false);
  assert.equal(paint(b, { x: 2, y: 2 }), true);
  assert.equal(paint(b, { x: 2, y: 2 }), false);
  assert.equal(paint(b, { x: 2, y: 2 }, true), true);
  assert.equal(b.walls.length, 0);
});
test('validates overlapping gates, IDs, boundary spans, and blocked approaches', () => {
  const b = blank(); b.gates = [gate(), gate({ start: 1 })];
  assert.ok(validate(b).errors.some(e => e.includes('overlaps')));
  assert.ok(validate(b).errors.some(e => e.includes('more than once')));
  b.gates = [gate({ start: 5 })]; assert.ok(validate(b).errors.some(e => e.includes('fit')));
  b.gates = [gate()]; b.walls = [{ x: 0, y: 0 }]; assert.throws(() => serialize(b));
});
test('invalid imported geometry is preserved for repair', () => {
  const b = blank(); b.gates = [gate({ start: 8 })];
  assert.equal(parseLayout(JSON.stringify(b)).board.gates[0].start, 8);
  assert.throws(() => serialize(b));
});
test('negative spans and blank strings cannot export', () => {
  for (const g of [gate({ length: -3 }), gate({ start: -1 }), gate({ id: ' ' }), gate({ type: '' })]) {
    const b = blank(); b.gates = [g]; assert.throws(() => serialize(b));
  }
});
test('all four gate sides match generator coordinate conventions', () => {
  const b = blank(8, 5);
  assert.deepEqual(edgeCell(b, 'top', 3), { x: 3, y: 0 });
  assert.deepEqual(edgeCell(b, 'bottom', 3), { x: 3, y: 4 });
  assert.deepEqual(edgeCell(b, 'left', 3), { x: 0, y: 3 });
  assert.deepEqual(edgeCell(b, 'right', 3), { x: 7, y: 3 });
  b.gates = [gate({ side: 'right', start: 1, length: 3 })];
  assert.equal(gateAt(b, 'right', 3), 0); assert.equal(gateAt(b, 'right', 4), -1);
});
test('resize crops only out-of-bounds walls and whole gates; source is intact', () => {
  const b = blank(); b.walls = [{ x: 5, y: 5 }, { x: 1, y: 1 }]; b.gates = [gate(), gate({ id: 'other', side: 'right', start: 4 })];
  const next = resize(b, 4, 4); assert.equal(next.walls.length, 1); assert.equal(next.gates.length, 1);
  assert.equal(b.walls.length, 2); assert.throws(() => resize(b, 1, 3));
});
test('history treats a paint stroke as one transaction and discards redo after a new edit', () => {
  const h = new History({ name: 'test', layout: blank() });
  const edit = structuredClone(h.current); paint(edit.layout, { x: 2, y: 2 }); paint(edit.layout, { x: 3, y: 2 }); h.commit(edit);
  assert.equal(h.past.length, 1); h.undo(); assert.equal(h.current.layout.walls.length, 0);
  h.redo(); assert.equal(h.current.layout.walls.length, 2); h.undo(); h.commit({ ...h.current, name: 'changed' }); assert.equal(h.redo(), false);
});
test('reports disconnected floors as advisory rather than a false solvability claim', () => {
  const b = blank(3, 3); b.walls = [{ x: 1, y: 0 }, { x: 1, y: 1 }, { x: 1, y: 2 }];
  assert.equal(validate(b).components, 2); assert.deepEqual(validate(b).errors, []);
});
test('duplicate walls can be repaired with eraser; IDs avoid collisions', () => {
  const b = blank(); b.walls = [{ x: 0, y: 0 }, { x: 0, y: 0 }]; assert.ok(validate(b).errors.length);
  paint(b, { x: 0, y: 0 }, true); assert.deepEqual(validate(b).errors, []);
  b.gates = [gate(), gate({ id: 'gate-2', side: 'bottom' })]; assert.equal(nextId(b), 'gate-3');
});
test('gate types come from the shared block type list', async () => {
  assert.ok(types.length > 0 && new Set(types.map(t => t.id)).size === types.length);
  const b = blank(); b.gates = [gate({ type: types[1].id })];
  assert.equal(JSON.parse(serialize(b, types)).gates[0].type, types[1].id);
  b.gates = [gate({ type: 'no-such-type' })];
  assert.ok(validate(b, types).errors.some(e => e.includes('unknown block type'))); assert.throws(() => serialize(b, types));
  for (const file of ['../examples/enclosed-layout.json', '../verification/exported-layout.json'])
    assert.deepEqual(validate(parseLayout(await readFile(new URL(file, import.meta.url), 'utf8')).board, types).errors, []);
});
test('old color layouts are rejected with a migration hint', () => {
  const old = { ...blank(), gates: [{ id: 'g', side: 'top', start: 0, length: 1, color: 'red' }] };
  assert.throws(() => parseLayout(JSON.stringify(old)), /migrate/);
});

