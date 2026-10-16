// Generates every level pool listed in curriculum.json, one after another, into levels/<pool>/:
//   node generate-pools.mjs [--curriculum curriculum.json] [--levels levels] [--pool name] [--jobs 8] [--seconds 180]
// Each pool is a generate-all.mjs run with the pool's layout filter, level count, retry limit and
// generator options. --pool runs just that pool. Reruns only top up what is missing.
import fs from 'node:fs';
import path from 'node:path';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const args = {};
for (let i = 2; i < process.argv.length; i += 2) args[process.argv[i].replace(/^--/, '')] = process.argv[i + 1];
const here = path.dirname(fileURLToPath(import.meta.url));
const curriculum = JSON.parse(fs.readFileSync(path.resolve(args.curriculum ?? path.join(here, 'curriculum.json')), 'utf8'));
const levelsDir = path.resolve(args.levels ?? path.join(here, 'levels'));
const names = args.pool ? [args.pool] : Object.keys(curriculum.pools);

for (const name of names) {
  const pool = curriculum.pools[name];
  if (!pool) throw new Error(`Unknown pool ${name}`);
  const options = Object.entries(pool.options ?? {}).flatMap(([key, value]) => [`--${key}`, String(value)]);
  const run = [path.join(here, 'generate-all.mjs'), '--out', path.join(levelsDir, name), '--only', pool.only ?? '',
    '--count', String(pool.count), '--maxAttempts', String(pool.maxAttempts ?? 0),
    '--jobs', args.jobs ?? '8', '--seconds', args.seconds ?? '180', ...options];
  console.log(`== pool ${name}: ${options.join(' ') || 'default options'}`);
  const code = await new Promise(resolve => spawn('node', run, { stdio: 'inherit' }).on('close', resolve));
  if (code) { console.error(`Pool ${name} failed with exit code ${code}`); process.exit(code); }
}
