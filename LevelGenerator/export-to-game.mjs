// Copies generated levels into the game in curriculum order:
//   node export-to-game.mjs [--curriculum curriculum.json] [--levels levels] [--game ../Game/BlocksEscape/Assets/Levels] [--seed 1] [--dry-run true]
// Levels come from levels/<pool>/<layout>/ (see generate-pools.mjs). The curriculum's stages are filled
// in order, each with the easiest levels from its pool that match its type counts and specials; after
// that the 'rest' pools follow in ascending difficulty until 'total' levels. A stage that can't be
// filled stops the export before anything in the game folder is touched.
// Files are named <level number>-<generated name>.json, e.g. 0001-0111-8x10_recessed_4types-003.json,
// so the game's catalog (sorted by file name) plays them in this order. Levels of the same layout
// stay at least 'gap' levels apart, across stage borders too: each slot takes one of the 'pick' easiest
// remaining candidates whose layout wasn't used in the last 'gap' slots, chosen at random (seeded).
// Existing level JSON files (and their .meta) in the game folder are deleted first; other assets stay.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = {};
for (let i = 2; i < process.argv.length; i += 2) args[process.argv[i].replace(/^--/, '')] = process.argv[i + 1];
const here = path.dirname(fileURLToPath(import.meta.url));
const curriculum = JSON.parse(fs.readFileSync(path.resolve(args.curriculum ?? path.join(here, 'curriculum.json')), 'utf8'));
const levelsDir = path.resolve(args.levels ?? path.join(here, 'levels'));
const gameDir = path.resolve(args.game ?? path.join(here, '../Game/BlocksEscape/Assets/Levels'));
const dryRun = args['dry-run'] === 'true';
const { total, gap = 8, pick = 3 } = curriculum;
let seed = Number(args.seed ?? 1);
const random = () => {
  // mulberry32
  seed = (seed + 0x6d2b79f5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};

const specialFeatures = { directional: 'directionalBlocks', frozen: 'frozenBlocks', layered: 'extraLayers', neutral: 'neutralBlocks' };
const pools = {};
function loadPool(name) {
  if (pools[name]) return pools[name];
  const levels = [];
  const root = path.join(levelsDir, name);
  if (!fs.existsSync(root)) throw new Error(`Pool folder ${root} is missing; run generate-pools.mjs first.`);
  for (const layout of fs.readdirSync(root)) {
    const folder = path.join(root, layout);
    if (!fs.statSync(folder).isDirectory()) continue;
    for (const f of fs.readdirSync(folder)) {
      const m = f.match(/^(\d{4})-(.+)-(\d{3})\.json$/);
      if (!m || m[2] !== layout) continue;
      const file = path.join(folder, f);
      const analysis = JSON.parse(fs.readFileSync(file, 'utf8')).analysis;
      const features = analysis.difficultyFeatures;
      const types = analysis.typeCounts ? Object.keys(analysis.typeCounts).length : Number(layout.match(/_(\d+)types/)?.[1]);
      const specials = Object.keys(specialFeatures).filter(s => features[specialFeatures[s]] > 0);
      levels.push({ file, name: f, layout, pool: name, difficulty: Number(m[1]), types, specials });
    }
  }
  levels.sort((a, b) => a.difficulty - b.difficulty || a.name.localeCompare(b.name));
  return pools[name] = levels;
}

const order = [];
const used = new Set();
// Appends count levels from candidates (sorted easiest first), keeping layouts gap levels apart.
function fill(candidates, count) {
  const remaining = candidates.filter(l => !used.has(l.file));
  let added = 0;
  while (added < count && remaining.length) {
    const recent = new Set(order.slice(-gap).map(l => l.layout));
    const eligible = [];
    for (let i = 0; i < remaining.length && eligible.length < pick; i++)
      if (!recent.has(remaining[i].layout)) eligible.push(i);
    // A stage with few layouts may have none eligible; fall back to its easiest level.
    const index = eligible.length ? eligible[Math.floor(random() * eligible.length)] : 0;
    const level = remaining.splice(index, 1)[0];
    order.push(level); used.add(level.file); added++;
  }
  return added;
}

const report = [];
for (const stage of curriculum.stages) {
  const allowed = new Set([...(stage.allow ?? []), ...(stage.requires ? [stage.requires] : [])]);
  const candidates = loadPool(stage.pool).filter(l =>
    (!stage.types || stage.types.includes(l.types)) &&
    (!stage.requires || l.specials.includes(stage.requires)) &&
    l.specials.every(s => allowed.has(s)));
  const start = order.length;
  const added = fill(candidates, stage.count);
  if (added < stage.count) {
    console.error(`Stage "${stage.name}" needs ${stage.count} levels but pool "${stage.pool}" has only ${added} matching ones. Nothing was exported.`);
    process.exit(1);
  }
  report.push({ stage: stage.name, levels: `${start + 1}-${order.length}`, slice: order.slice(start) });
}
const restStart = order.length;
const rest = curriculum.rest.pools.flatMap(loadPool).sort((a, b) => a.difficulty - b.difficulty || a.name.localeCompare(b.name));
fill(rest, total - order.length);
if (order.length < total) console.warn(`Only ${order.length} of ${total} levels available.`);
report.push({ stage: 'rest', levels: `${restStart + 1}-${order.length}`, slice: order.slice(restStart) });

for (const { stage, levels, slice } of report) {
  if (!slice.length) continue;
  const types = [...new Set(slice.map(l => l.types))].sort().join('/');
  const specials = Object.keys(specialFeatures).map(s => `${s} ${slice.filter(l => l.specials.includes(s)).length}`).join(', ');
  console.log(`${levels.padEnd(10)} ${stage.padEnd(12)} types ${types.padEnd(8)} difficulty ${Math.min(...slice.map(l => l.difficulty))}-${Math.max(...slice.map(l => l.difficulty))}  ${specials}`);
}

let minGap = Infinity, lastSeen = {};
order.forEach((l, i) => {
  if (l.layout in lastSeen) minGap = Math.min(minGap, i - lastSeen[l.layout]);
  lastSeen[l.layout] = i;
});
console.log(`Closest repeat of a layout: ${minGap} levels apart.`);
if (dryRun) { console.log('Dry run: game folder left unchanged.'); process.exit(0); }

fs.mkdirSync(gameDir, { recursive: true });
let removed = 0;
for (const f of fs.readdirSync(gameDir))
  if (/\.json(\.meta)?$/.test(f)) { fs.rmSync(path.join(gameDir, f)); removed++; }
const digits = Math.max(4, String(order.length).length);
order.forEach((l, i) => fs.copyFileSync(l.file, path.join(gameDir, `${String(i + 1).padStart(digits, '0')}-${l.name}`)));
console.log(`Removed ${removed} old files, wrote ${order.length} levels to ${gameDir}`);
