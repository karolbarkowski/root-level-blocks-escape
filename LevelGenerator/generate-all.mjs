// Generates levels for every layout until each has --count of them:
//   node generate-all.mjs [--layouts layouts] [--out levels] [--count 10] [--seconds 180] [--jobs 8] [--maxAttempts 0] [--only regex]
// Each layout gets its own folder, levels/<layout>/. A layout that comes up short goes to
// the back of the queue and is retried after the others, with a new seed and a longer time
// limit. Every run gets a fresh random seed (logged), and a level whose blocks match an
// earlier one in the same folder is deleted, so reruns never add copies. Existing levels
// count towards the target, so the script can be stopped and rerun.
// --only limits the run to layouts whose name matches the regex, e.g. _[45]types.
// --maxAttempts 0 retries forever. Progress is appended to <out>/generate-all.log.
// Any other option (e.g. --type-spread 2 --frozen false) is passed through to every generate run.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const args = {};
for (let i = 2; i < process.argv.length; i += 2) args[process.argv[i].replace(/^--/, '')] = process.argv[i + 1];
const here = path.dirname(fileURLToPath(import.meta.url));
const layoutsDir = path.resolve(args.layouts ?? path.join(here, 'layouts'));
const outDir = path.resolve(args.out ?? path.join(here, 'levels'));
const count = Number(args.count ?? 10);
const seconds = Number(args.seconds ?? 180);
const jobs = Number(args.jobs ?? 8);
const maxAttempts = Number(args.maxAttempts ?? 0);
const only = new RegExp(args.only ?? '');
const dll = path.join(here, 'bin/Release/net10.0/LevelGenerator.dll');
const own = ['layouts', 'out', 'count', 'seconds', 'jobs', 'maxAttempts', 'only'];
const passThrough = Object.entries(args).filter(([key]) => !own.includes(key)).flatMap(([key, value]) => [`--${key}`, value]);

fs.mkdirSync(outDir, { recursive: true });
const logFile = path.join(outDir, 'generate-all.log');
const log = line => {
  const text = `${new Date().toISOString().slice(11, 19)} ${line}`;
  console.log(text);
  fs.appendFileSync(logFile, text + '\n');
};

const levelFiles = name => {
  const folder = path.join(outDir, name);
  if (!fs.existsSync(folder)) return [];
  const pattern = new RegExp(`^\\d{4}-${name}-(\\d{3})\\.json$`);
  return fs.readdirSync(folder).filter(f => pattern.test(f))
    .sort((a, b) => a.match(pattern)[1] - b.match(pattern)[1]);
};

// Deletes later copies of a level (same starting blocks), with their SVG and HTML.
function removeDuplicates(name) {
  const folder = path.join(outDir, name);
  const seen = new Set();
  let removed = 0;
  for (const f of levelFiles(name)) {
    const key = JSON.stringify(JSON.parse(fs.readFileSync(path.join(folder, f), 'utf8')).blocks);
    if (!seen.has(key)) { seen.add(key); continue; }
    for (const ext of ['.json', '.svg', '.html']) fs.rmSync(path.join(folder, f.slice(0, -5) + ext), { force: true });
    removed++;
  }
  if (removed) log(`dedup ${name}: removed ${removed} duplicate level(s)`);
  return levelFiles(name).length;
}

function generate(name, missing, attempt) {
  // Double the time limit on each retry, up to 8x.
  const limit = seconds * 2 ** Math.min(attempt, 3);
  const seed = Math.floor(Math.random() * 2 ** 31);
  return new Promise(resolve => {
    const child = spawn('dotnet', [dll, 'generate', '--layout', path.join(layoutsDir, `${name}.json`),
      '--maxCandidates', String(missing), '--seed', String(seed), '--out', path.join(outDir, name), ...passThrough]);
    let tail = '';
    const keep = d => { tail = (tail + d).slice(-400); };
    child.stdout.on('data', keep);
    child.stderr.on('data', keep);
    const timer = setTimeout(() => child.kill(), limit * 1000);
    child.on('close', code => {
      clearTimeout(timer);
      resolve({ limit, seed, code, last: tail.trim().split(/\r?\n/).pop() ?? '' });
    });
  });
}

const queue = fs.readdirSync(layoutsDir).filter(f => f.endsWith('.json') && only.test(f.slice(0, -5))).sort()
  .map(f => ({ name: f.slice(0, -5), attempt: 0 }));
const total = queue.length;
const gaveUp = [];
let running = 0, finished = 0;
log(`Start: ${total} layouts, ${count} levels each, ${jobs} jobs, ${seconds}s base limit${passThrough.length ? `, generator options ${passThrough.join(' ')}` : ''}`);

async function worker() {
  while (true) {
    const item = queue.shift();
    if (!item) {
      // Other workers may still requeue a layout; wait for them before quitting.
      if (running === 0) return;
      await new Promise(r => setTimeout(r, 1000));
      continue;
    }
    const have = removeDuplicates(item.name);
    if (have >= count) { finished++; continue; }
    running++;
    const r = await generate(item.name, count - have, item.attempt);
    running--;
    const now = removeDuplicates(item.name);
    if (now >= count) {
      finished++;
      log(`done  ${item.name}: ${now}/${count} (attempt ${item.attempt + 1}) [${finished}/${total}]`);
    } else if (maxAttempts && item.attempt + 1 >= maxAttempts) {
      gaveUp.push(item.name);
      log(`stop  ${item.name}: ${now}/${count} after ${item.attempt + 1} attempts. ${r.last}`);
    } else {
      queue.push({ name: item.name, attempt: item.attempt + 1 });
      log(`retry ${item.name}: ${now}/${count} after ${r.limit}s, seed ${r.seed}. ${r.last}`);
    }
  }
}

await Promise.all(Array.from({ length: jobs }, worker));
const short = queue.map(q => q.name).concat(gaveUp);
log(short.length ? `Finished with ${short.length} layouts short: ${short.join(', ')}` : `Finished: every layout has ${count} levels`);
