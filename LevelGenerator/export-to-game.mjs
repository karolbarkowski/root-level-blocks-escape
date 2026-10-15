// Copies every generated level into the game, in ascending difficulty:
//   node export-to-game.mjs [--levels levels] [--game ../Game/BlocksEscape/Assets/Levels] [--gap 8] [--pick 3] [--seed 1]
// Files are named <level number>-<generated name>.json, e.g. 0001-0111-8x10_recessed_4types-003.json,
// so the game's catalog (sorted by file name) plays them in this order. Levels of the same layout
// stay at least --gap levels apart: each slot takes one of the --pick easiest remaining levels whose
// layout wasn't used in the last --gap slots, chosen at random (seeded, so the order is reproducible).
// Existing level JSON files (and their .meta) in the game folder are deleted first; other assets stay.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = {};
for (let i = 2; i < process.argv.length; i += 2) args[process.argv[i].replace(/^--/, '')] = process.argv[i + 1];
const here = path.dirname(fileURLToPath(import.meta.url));
const levelsDir = path.resolve(args.levels ?? path.join(here, 'levels'));
const gameDir = path.resolve(args.game ?? path.join(here, '../Game/BlocksEscape/Assets/Levels'));
const gap = Number(args.gap ?? 8);
const pick = Number(args.pick ?? 3);
let seed = Number(args.seed ?? 1);
const random = () => {
  // mulberry32
  seed = (seed + 0x6d2b79f5) | 0;
  let t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
  t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
};

const levels = [];
for (const layout of fs.readdirSync(levelsDir)) {
  const folder = path.join(levelsDir, layout);
  if (!fs.statSync(folder).isDirectory()) continue;
  for (const f of fs.readdirSync(folder)) {
    const m = f.match(/^(\d{4})-(.+)-(\d{3})\.json$/);
    if (m && m[2] === layout) levels.push({ file: path.join(folder, f), name: f, layout, difficulty: Number(m[1]) });
  }
}
levels.sort((a, b) => a.difficulty - b.difficulty || a.name.localeCompare(b.name));

const order = [];
const remaining = [...levels];
while (remaining.length) {
  const recent = new Set(order.slice(-gap).map(l => l.layout));
  const eligible = [];
  for (let i = 0; i < remaining.length && eligible.length < pick; i++)
    if (!recent.has(remaining[i].layout)) eligible.push(i);
  // Near the end only a few layouts may be left; fall back to the easiest level.
  const index = eligible.length ? eligible[Math.floor(random() * eligible.length)] : 0;
  order.push(remaining.splice(index, 1)[0]);
}

fs.mkdirSync(gameDir, { recursive: true });
let removed = 0;
for (const f of fs.readdirSync(gameDir))
  if (/\.json(\.meta)?$/.test(f)) { fs.rmSync(path.join(gameDir, f)); removed++; }
const digits = Math.max(4, String(order.length).length);
order.forEach((l, i) => fs.copyFileSync(l.file, path.join(gameDir, `${String(i + 1).padStart(digits, '0')}-${l.name}`)));

let minGap = Infinity, maxDrop = 0, lastSeen = {};
order.forEach((l, i) => {
  if (l.layout in lastSeen) minGap = Math.min(minGap, i - lastSeen[l.layout]);
  lastSeen[l.layout] = i;
  if (i) maxDrop = Math.max(maxDrop, order[i - 1].difficulty - l.difficulty);
});
console.log(`Removed ${removed} old files, wrote ${order.length} levels to ${gameDir}`);
console.log(`Difficulty ${order[0].difficulty} -> ${order[order.length - 1].difficulty}; closest repeat of a layout: ${minGap} levels apart; largest step down: ${maxDrop}`);
