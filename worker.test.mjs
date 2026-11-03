import { test } from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { spawn } from 'node:child_process';
import { once } from 'node:events';
import { fileURLToPath } from 'node:url';
import { normalizeLimits } from './probe.mjs';
import { UsageState, STALE_AFTER_MS } from './usage-state.mjs';
import { UsageWorker, parseArguments } from './worker.mjs';

// Synthetic test values, unrelated to any real account observation.
const quota = (used = 25, more = {}) => ({ rateLimitsByLimitId: {
  codex: { limitId: 'codex', primary: { usedPercent: used, windowDurationMins: 10080, resetsAt: 1700000000 } },
  ...more,
} });

function harness(outcomes) {
  let now = Date.parse('2020-01-01T00:00:00Z');
  const states = [];
  const clients = [];
  const methods = [];
  const worker = new UsageWorker({
    now: () => now,
    emit: state => states.push(state),
    createClient: () => {
      const client = {
        lines: new EventEmitter(), exited: false, closed: false,
        connect: async () => {},
        request: async method => {
          methods.push(method);
          const result = outcomes.shift();
          if (result instanceof Error) throw result;
          return typeof result === 'function' ? result() : result;
        },
        close: async () => { client.closed = true; client.exited = true; return true; },
      };
      clients.push(client);
      return client;
    },
  });
  worker.start({ timers: false });
  return { worker, states, clients, methods, advance: ms => { now += ms; } };
}

test('no success means unavailable rather than zero usage or fabricated 100% remaining', () => {
  const state = new UsageState();
  state.begin(0);
  assert.equal(state.success(normalizeLimits({}), 0).status, 'unavailable');
  assert.deepEqual(state.snapshot().windows, []);
  assert.equal(state.snapshot().observedAt, null);
});

test('failure retains last observation as stale; recovery replaces it and clears the failure', () => {
  const state = new UsageState();
  state.begin(0);
  state.success(normalizeLimits(quota()), 0);
  state.begin(60000);
  const failed = state.failure(new Error('request_timeout'));
  assert.equal(failed.status, 'stale');
  assert.equal(failed.reason, 'request_timeout');
  assert.equal(failed.windows[0].remainingPercent, 75);
  assert.equal(failed.observedAt, '1970-01-01T00:00:00.000Z');
  assert.equal(failed.attemptedAt, '1970-01-01T00:01:00.000Z');
  state.begin(120000);
  const recovered = state.success(normalizeLimits(quota(30)), 120000);
  assert.equal(recovered.status, 'ok');
  assert.equal(recovered.windows[0].remainingPercent, 70);
  assert.equal(recovered.reason, null);
});

test('three-minute-old observations expire while inactive without changing their timestamp', () => {
  const state = new UsageState();
  state.success(normalizeLimits(quota()), 1000);
  assert.equal(state.expire(STALE_AFTER_MS), null);
  const expired = state.expire(STALE_AFTER_MS + 1000);
  assert.equal(expired.status, 'stale');
  assert.equal(expired.reason, 'last_update_expired');
  assert.equal(expired.observedAt, '1970-01-01T00:00:01.000Z');
  assert.equal(state.expire(STALE_AFTER_MS * 2), null);
});

test('a slow refresh cannot keep a three-minute-old observation looking current', () => {
  const state = new UsageState();
  state.success(normalizeLimits(quota()), 0);
  state.begin(STALE_AFTER_MS - 10000);
  const expired = state.expire(STALE_AFTER_MS);
  assert.equal(expired.status, 'stale');
  assert.equal(expired.reason, 'last_update_expired');
});

test('multiple quota windows are preserved and status objects cannot mutate the state cache', () => {
  const state = new UsageState();
  const result = state.success(normalizeLimits(quota(25, {
    other: { primary: { usedPercent: 10 }, secondary: { usedPercent: 25 } },
  })), 0);
  assert.deepEqual(result.windows.map(window => [window.limitId, window.window, window.remainingPercent]),
    [['codex', 'primary', 75], ['other', 'primary', 90], ['other', 'secondary', 75]]);
  result.windows[0].remainingPercent = 999;
  assert.equal(state.snapshot().windows[0].remainingPercent, 75);
});

test('unknown exception text never leaves the worker state', () => {
  const state = new UsageState();
  assert.equal(state.failure(new Error('private email or token')).reason, 'query_failed');
  assert.equal(state.failure(new Error('rpc_error_-32000')).reason, 'rpc_error_-32000');
});

test('polling pauses in background and resumes only when a new read is due', async () => {
  const h = harness([quota(), quota(30), quota(35)]);
  assert.equal(h.states[0].status, 'loading');
  await h.worker.tick();
  assert.equal(h.methods.length, 0);
  await h.worker.setActive(true);
  assert.equal(h.states.at(-1).windows[0].remainingPercent, 75);
  h.advance(59000);
  await h.worker.tick();
  assert.equal(h.methods.length, 1);
  h.advance(1000);
  await h.worker.tick();
  assert.equal(h.methods.length, 2);
  await h.worker.setActive(false);
  h.advance(180000);
  await h.worker.tick();
  assert.equal(h.methods.length, 2);
  assert.equal(h.states.at(-1).status, 'stale');
  await h.worker.setActive(true);
  assert.equal(h.methods.length, 3);
  assert.deepEqual(new Set(h.methods), new Set(['account/rateLimits/read']));
  await h.worker.stop();
  assert.ok(h.clients.every(client => client.closed));
});

test('manual requests are throttled and concurrent requests coalesce', async () => {
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const h = harness([() => pending, quota(30)]);
  const first = h.worker.refresh();
  const second = h.worker.refresh();
  assert.equal(first, second);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(h.methods.length, 1);
  release(quota());
  await first;
  h.advance(4999);
  assert.equal(await h.worker.refresh(), false);
  h.advance(1);
  await h.worker.refresh();
  assert.equal(h.methods.length, 2);
  await h.worker.stop();
});

test('transport failure is stale and a later retry reconnects to a new client', async () => {
  const h = harness([quota(), new Error('process_exited'), quota(30)]);
  await h.worker.setActive(true);
  h.advance(60000);
  await h.worker.tick();
  assert.equal(h.states.at(-1).status, 'stale');
  assert.equal(h.clients[0].closed, true);
  h.advance(60000);
  await h.worker.tick();
  assert.equal(h.clients.length, 2);
  assert.equal(h.states.at(-1).status, 'ok');
  await h.worker.stop();
});

test('an auth change clears cached values instead of reusing the previous account quota', async () => {
  const h = harness([quota()]);
  await h.worker.setActive(true);
  h.clients[0].lines.emit('line', JSON.stringify({ method: 'account/updated', params: { authMode: null } }));
  assert.equal(h.states.at(-1).reason, 'account_changed');
  assert.equal(h.states.at(-1).status, 'unavailable');
  assert.deepEqual(h.states.at(-1).windows, []);
  assert.equal(h.states.at(-1).observedAt, null);
  await h.worker.stop();
});

test('argument parser accepts paths with spaces and rejects invalid parent process IDs', () => {
  assert.deepEqual(parseArguments(['--codex', 'C:\\Program Files\\Codex\\codex.exe', '--parent', '123']),
    { executable: 'C:\\Program Files\\Codex\\codex.exe', parentPid: 123 });
  for (const args of [['--parent', '0'], ['--parent', 'oops'], ['--other']]) {
    assert.throws(() => parseArguments(args), /invalid_arguments/);
  }
});

test('real worker process exits on stdin EOF without starting an app server', { timeout: 6000 }, async () => {
  const child = spawn(process.execPath, [fileURLToPath(new URL('./worker.mjs', import.meta.url))], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
  });
  const exited = once(child, 'exit');
  let output = '';
  child.stdout.on('data', chunk => { output += chunk; });
  await once(child.stdout, 'data');
  child.stdin.end();
  const [code] = await exited;
  assert.equal(code, 0);
  assert.deepEqual(output.trim().split('\n').map(line => JSON.parse(line).status), ['loading']);
});

test('real worker process exits when the supplied parent no longer exists', { timeout: 6000 }, async () => {
  const child = spawn(process.execPath, [fileURLToPath(new URL('./worker.mjs', import.meta.url)), '--parent', '2147483647'], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
  });
  const exited = once(child, 'exit');
  let output = '';
  child.stdout.on('data', chunk => { output += chunk; });
  const [code] = await exited;
  assert.equal(code, 0);
  assert.equal(JSON.parse(output.trim()).status, 'loading');
});

test('malformed control frames cannot terminate the worker or invoke another RPC method', { timeout: 6000 }, async () => {
  const child = spawn(process.execPath, [fileURLToPath(new URL('./worker.mjs', import.meta.url))], {
    windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'],
  });
  const exited = once(child, 'exit');
  let output = '';
  child.stdout.on('data', chunk => { output += chunk; });
  await once(child.stdout, 'data');
  child.stdin.write('null\n[]\n17\ninvalid JSON\n{"method":"turn/start"}\n{"command":"quit"}\n');
  const [code] = await exited;
  assert.equal(code, 0);
  assert.deepEqual(output.trim().split('\n').map(line => JSON.parse(line).status), ['loading']);
});
