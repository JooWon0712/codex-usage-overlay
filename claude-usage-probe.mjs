import { spawn } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { mkdirSync, readFileSync, renameSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { homedir } from 'node:os';
import path from 'node:path';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

const MAX_OUTPUT_BYTES = 1024 * 1024;
const MAX_SNAPSHOT_BYTES = 4096;
const TIMEOUT_MS = 20000;
const PERIOD_KEYS = ['fiveHour', 'weekly'];
const SUBSCRIPTION_TYPES = new Set(['free', 'pro', 'max', 'ultra', 'team', 'enterprise']);
const AUTH_ARGS = ['--setting-sources', '', 'auth', 'status', '--json'];
const USAGE_ARGS = [
  '-p', '--output-format', 'stream-json', '--verbose', '--max-turns', '1',
  '--no-session-persistence', '--setting-sources', '', '--settings', '{"disableAllHooks":true}',
  '--strict-mcp-config', '--tools', '', '--disallowed-tools', '*', '--', '/usage',
];

function isAbsolute(value) {
  return typeof value === 'string' && path.isAbsolute(value)
    && (process.platform !== 'win32' || path.parse(value).root.length > 1);
}

function nativeExecutable(value) {
  if (!isAbsolute(value) || (process.platform === 'win32' && path.extname(value).toLowerCase() !== '.exe')) return false;
  try { return statSync(value).isFile(); }
  catch { return false; }
}

export function resolveClaude(env = process.env, home = homedir()) {
  const command = process.platform === 'win32' ? 'claude.exe' : 'claude';
  if (env.CODEX_USAGE_OVERLAY_CLAUDE) {
    return nativeExecutable(env.CODEX_USAGE_OVERLAY_CLAUDE) ? env.CODEX_USAGE_OVERLAY_CLAUDE : null;
  }
  const pathValue = Object.entries(env).find(([key]) => key.toUpperCase() === 'PATH')?.[1] ?? '';
  const pathDirectories = pathValue.split(path.delimiter).map(value => value.replace(/^"(.*)"$/, '$1')).filter(isAbsolute);
  return [path.join(env.USERPROFILE || home, '.local', 'bin', command), ...pathDirectories.map(directory => path.join(directory, command))]
    .find(nativeExecutable) ?? null;
}

export function makeProbeEnvironment(env = process.env) {
  const excluded = new Set([
    'ANTHROPIC_API_KEY', 'ANTHROPIC_AUTH_TOKEN', 'ANTHROPIC_BASE_URL', 'ANTHROPIC_CUSTOM_HEADERS',
    'CLAUDE_CODE_OAUTH_TOKEN', 'CLAUDE_CODE_OAUTH_TOKEN_FD', 'CLAUDE_CODE_API_KEY_FD',
    'CLAUDE_CODE_USE_BEDROCK', 'CLAUDE_CODE_USE_VERTEX', 'CLAUDE_CODE_USE_FOUNDRY',
    'CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC', 'CLAUDE_CODE_DISABLE_AUTO_MEMORY',
  ]);
  const result = Object.fromEntries(Object.entries(env).filter(([key]) => !excluded.has(key.toUpperCase())));
  result.CLAUDE_CODE_DISABLE_AUTO_MEMORY = '1';
  return result;
}

function parseJson(text) {
  try { return JSON.parse(text); }
  catch { return null; }
}

function parseReset(reset, nowMs) {
  if (reset === null) return null;
  if (typeof reset !== 'string' || !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(reset)) return undefined;
  const seconds = Math.floor(Date.parse(reset) / 1000);
  return Number.isSafeInteger(seconds) && seconds <= 253402300799 && seconds * 1000 > nowMs ? seconds : undefined;
}

function normalizeRows(rows, kind, group, nowMs) {
  const matching = rows.filter(row => row?.kind === kind && row.group === group && row.scope == null);
  if (matching.length !== 1) return null;
  const { percent, resets_at: reset } = matching[0];
  if (!Number.isFinite(percent) || percent < 0 || percent > 100) return null;
  const resetsAt = parseReset(reset, nowMs);
  if (resetsAt === undefined) return null;
  return { usedPercentage: percent, resetsAt, observedAt: new Date(nowMs).toISOString() };
}

export function readUsageOutput(stdout, nowMs = Date.now()) {
  const events = parseEvents(stdout);
  if (events.some(event => !event || typeof event.type !== 'string')) return null;
  const results = events.filter(event => event.type === 'result');
  const result = results[0];
  if (results.length !== 1 || events.at(-1) !== result || result.subtype !== 'success' || result.is_error !== false
    || result.local_command !== 'usage' || result.num_turns !== 0 || result.duration_api_ms !== 0
    || result.usage?.input_tokens !== 0 || result.usage?.output_tokens !== 0
    || (result.usage.cache_creation_input_tokens ?? 0) !== 0 || (result.usage.cache_read_input_tokens ?? 0) !== 0
    || Object.keys(result.modelUsage ?? {}).length !== 0) return null;

  const reports = events.filter(event => event.type === 'assistant' && event.usage_report != null);
  const rows = reports[0]?.usage_report?.rate_limits?.limits;
  if (reports.length !== 1 || !Array.isArray(rows)) return null;
  const fiveHour = normalizeRows(rows, 'session', 'session', nowMs);
  const weekly = normalizeRows(rows, 'weekly_all', 'weekly', nowMs);
  return fiveHour || weekly ? { fiveHour, weekly } : null;
}

function parseEvents(stdout) {
  return stdout.trim().split(/\r?\n/).filter(Boolean).map(parseJson);
}

function storedWindow(window, nowMs) {
  const observedMs = Date.parse(window?.observedAt ?? '');
  if (!Number.isFinite(window?.usedPercentage) || window.usedPercentage < 0 || window.usedPercentage > 100
    || !Number.isSafeInteger(window.resetsAt) || window.resetsAt > 253402300799 || window.resetsAt * 1000 <= nowMs
    || !Number.isFinite(observedMs) || observedMs > nowMs) return null;
  return { usedPercentage: window.usedPercentage, resetsAt: window.resetsAt, observedAt: new Date(observedMs).toISOString() };
}

export function makeSnapshot({ fresh, previous, accountKey, subscriptionType, status, attemptedMs, nowMs }) {
  const result = { attemptedAt: new Date(attemptedMs).toISOString(), status };
  if (accountKey) result.accountKey = accountKey;
  const plan = normalizeSubscriptionType(subscriptionType);
  if (accountKey && plan && (status === 'ok' || status === 'partial' || status === 'unavailable'))
    result.subscriptionType = plan;
  for (const key of PERIOD_KEYS) {
    const last = accountKey && previous?.accountKey === accountKey ? storedWindow(previous[key], nowMs) : null;
    result[key] = fresh?.[key] ?? last;
  }
  return result;
}

function readPrior(file) {
  try { return statSync(file).size <= MAX_SNAPSHOT_BYTES ? parseJson(readFileSync(file, 'utf8')) : null; }
  catch { return null; }
}

function atomicWrite(file, value) {
  const directory = path.dirname(file);
  mkdirSync(directory, { recursive: true });
  const temporary = path.join(directory, '.claude-usage-' + randomUUID() + '.tmp');
  try {
    writeFileSync(temporary, JSON.stringify(value) + '\n', { encoding: 'utf8', flag: 'wx', mode: 0o600 });
    renameSync(temporary, file);
  } finally {
    rmSync(temporary, { force: true });
  }
}

function capture(executable, args, env, deadline) {
  const timeout = deadline - Date.now();
  if (timeout <= 0) return Promise.resolve({ code: null, stdout: '' });
  return new Promise(resolve => {
    const child = spawn(executable, args, { env, windowsHide: true, stdio: ['ignore', 'pipe', 'ignore'] });
    const chunks = [];
    let size = 0;
    let overLimit = false;
    const timer = setTimeout(() => { overLimit = true; child.kill(); }, timeout);
    child.stdout.on('data', chunk => {
      size += chunk.length;
      if (size > MAX_OUTPUT_BYTES) {
        if (!overLimit) child.kill();
        overLimit = true;
      } else if (!overLimit) {
        chunks.push(chunk);
      }
    });
    child.once('error', error => {
      clearTimeout(timer);
      resolve({ code: null, stdout: '', missing: error.code === 'ENOENT' });
    });
    child.once('close', code => {
      clearTimeout(timer);
      resolve({ code: overLimit ? null : code, stdout: overLimit ? '' : Buffer.concat(chunks).toString('utf8') });
    });
  });
}

function supportedVersion(text) {
  const match = /^2\.(\d+)\.(\d+)\s+\(Claude Code\)\s*$/.exec(text.trim());
  return match && (Number(match[1]) > 1 || (Number(match[1]) === 1 && Number(match[2]) >= 273));
}

function normalizeSubscriptionType(value) {
  if (typeof value !== 'string') return null;
  const normalized = value.trim().toLowerCase();
  return SUBSCRIPTION_TYPES.has(normalized) ? normalized : null;
}

function readAuthentication(stdout) {
  const auth = parseJson(stdout);
  if (auth?.loggedIn !== true || auth.authMethod !== 'claude.ai') return null;
  const id = auth.accountId ?? auth.email;
  const accountKey = typeof id === 'string' && id.trim()
    ? createHash('sha256').update(JSON.stringify([id.trim().toLowerCase(), auth.orgId ?? null, auth.configDirectory ?? null])).digest('hex')
    : null;
  return { accountKey, subscriptionType: normalizeSubscriptionType(auth.subscriptionType) };
}

export async function collectUsage(executable, env, deadline, run = capture, now = Date.now) {
  if (!executable) return { status: 'missing-cli' };
  const version = await run(executable, ['--version'], env, deadline);
  if (version.missing) return { status: 'missing-cli' };
  if (version.code !== 0 || !supportedVersion(version.stdout)) return { status: 'unavailable' };
  const before = await run(executable, AUTH_ARGS, env, deadline);
  const auth = readAuthentication(before.stdout);
  if (!auth) return { status: parseJson(before.stdout)?.loggedIn !== undefined ? 'auth-required' : 'unavailable' };

  const usage = await run(executable, USAGE_ARGS, env, deadline);
  if (parseEvents(usage.stdout).some(event => event?.type === 'assistant' && event.error === 'authentication_failed'))
    return { status: 'auth-required' };
  const fresh = usage.code === 0 ? readUsageOutput(usage.stdout, now()) : null;
  const after = await run(executable, AUTH_ARGS, env, deadline);
  const confirmed = readAuthentication(after.stdout);
  if (!confirmed) return { status: parseJson(after.stdout)?.loggedIn !== undefined ? 'auth-required' : 'unavailable' };
  if (auth.accountKey !== confirmed.accountKey) return { status: 'unavailable' };
  const observation = { fresh, accountKey: auth.accountKey, status: !fresh ? 'unavailable' : (fresh.fiveHour && fresh.weekly ? 'ok' : 'partial') };
  if (auth.accountKey && auth.subscriptionType && auth.subscriptionType === confirmed.subscriptionType
    && before.code === 0 && after.code === 0)
    observation.subscriptionType = auth.subscriptionType;
  return observation;
}

async function waitForStart() {
  const input = createInterface({ input: process.stdin, terminal: false, crlfDelay: Infinity });
  try {
    for await (const line of input) return line.trim() === 'start';
    return false;
  } finally {
    input.close();
    process.stdin.pause();
  }
}

async function runClaude(executable, args, directory) {
  const boundary = args.indexOf('--');
  const options = boundary === -1 ? args : args.slice(0, boundary);
  if (!executable || !options.some(value => value === '-p' || value === '--print')) {
    process.stderr.write('Claude Code 실행 경로와 -p 옵션이 필요합니다.\n');
    return 1;
  }
  return new Promise(resolve => {
    const child = spawn(executable, args, { windowsHide: true, stdio: 'inherit' });
    let started = false;
    child.once('spawn', () => { started = true; });
    child.once('error', () => {
      process.stderr.write('Claude Code를 시작할 수 없습니다.\n');
      resolve(1);
    });
    child.once('close', code => {
      if (started) {
        try { atomicWrite(path.join(directory, 'claude-refresh-request.json'), { requestedAt: new Date().toISOString() }); }
        catch { process.stderr.write('Claude Code 갱신 알림을 기록할 수 없습니다.\n'); }
      }
      resolve(code ?? 1);
    });
  });
}

async function main(args) {
  const local = process.env.LOCALAPPDATA;
  if (!isAbsolute(local)) throw new Error('Data directory unavailable');
  const directory = path.join(local, 'CodexUsageOverlay');
  if (args[0] === '--run') return runClaude(resolveClaude(), args.slice(1), directory);
  if (args.length) {
    if (args.length !== 1 || args[0] !== '--wait-for-start' || !await waitForStart()) return 1;
  }
  const attemptedMs = Date.now();
  const file = path.join(directory, 'claude-usage-auto.json');
  const previous = readPrior(file);
  const result = await collectUsage(resolveClaude(), makeProbeEnvironment(), attemptedMs + TIMEOUT_MS);
  atomicWrite(file, makeSnapshot({ ...result, previous, attemptedMs, nowMs: Date.now() }));
  return result.status === 'ok' || result.status === 'partial' ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { process.exitCode = await main(process.argv.slice(2)); }
  catch {
    process.stderr.write('Claude Code 한도 갱신을 완료할 수 없습니다.\n');
    process.exitCode = 1;
  }
}
