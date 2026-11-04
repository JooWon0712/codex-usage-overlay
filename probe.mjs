import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';

let koreaResetFormatter;

function formatResetKorea(seconds) {
  if (!Number.isFinite(seconds)) return null;
  const reset = new Date(seconds * 1000);
  if (!Number.isFinite(reset.getTime())) return null;
  koreaResetFormatter ??= new Intl.DateTimeFormat('sv-SE', {
    timeZone: 'Asia/Seoul', dateStyle: 'short', timeStyle: 'medium',
  });
  return koreaResetFormatter.format(reset) + ' KST';
}

// Read-only methods only. Never starts a conversation or loads a model.
export class UsageClient {
  constructor(executable = 'codex.exe') {
    this.executable = executable;
    this.nextId = 1;
    this.pending = new Map();
  }

  async connect() {
    this.child = spawn(this.executable, ['app-server', '--listen', 'stdio://'], {
      windowsHide: true,
      stdio: ['pipe', 'pipe', 'pipe'],
    });
    // Do not print logs, account identifiers, credentials, or server requests.
    this.child.stderr.resume();
    this.lines = createInterface({ input: this.child.stdout });
    this.child.stdout.on('error', () => this.failAll('process_read_failed'));
    this.lines.on('error', () => this.failAll('process_read_failed'));
    this.lines.on('line', line => {
      let message;
      try { message = JSON.parse(line); } catch { return; }
      const request = this.pending.get(message.id);
      if (!request || message.method) return;
      this.pending.delete(message.id);
      clearTimeout(request.timer);
      if (message.error) request.reject(new Error(`rpc_error_${message.error.code}`));
      else request.resolve(message.result);
    });
    this.child.stdin.on('error', () => this.failAll('process_write_failed'));
    this.child.on('error', () => {
      this.exited = true;
      this.failAll('process_start_failed');
    });
    this.child.on('exit', () => {
      this.exited = true;
      this.failAll('process_exited');
    });
    await this.request('initialize', {
      clientInfo: { name: 'personal_usage_probe', title: 'Personal Usage Probe', version: '0.1.0' },
    });
    this.child.stdin.write(JSON.stringify({ method: 'initialized' }) + '\n');
  }

  failAll(reason) {
    for (const request of this.pending.values()) {
      clearTimeout(request.timer);
      request.reject(new Error(reason));
    }
    this.pending.clear();
  }

  request(method, params) {
    if (!this.child || this.exited) return Promise.reject(new Error('process_not_running'));
    if (!['initialize', 'account/rateLimits/read'].includes(method)) {
      throw new Error('method_not_allowed');
    }
    return new Promise((resolve, reject) => {
      const id = this.nextId++;
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('request_timeout'));
      }, 20000);
      this.pending.set(id, { resolve, reject, timer });
      const message = { id, method };
      if (params !== undefined) message.params = params;
      this.child.stdin.write(JSON.stringify(message) + '\n', error => {
        if (error && this.pending.delete(id)) {
          clearTimeout(timer);
          reject(new Error('process_write_failed'));
        }
      });
    });
  }

  async read() {
    return normalizeLimits(await this.request('account/rateLimits/read'));
  }

  async close() {
    this.failAll('client_closed');
    if (!this.child || this.exited) {
      this.lines?.close();
      return true;
    }
    const waitForExit = milliseconds => new Promise(resolve => {
      if (this.exited) return resolve();
      const finish = () => {
        clearTimeout(timer);
        this.child.off('exit', finish);
        this.child.off('error', finish);
        resolve();
      };
      const timer = setTimeout(finish, milliseconds);
      this.child.once('exit', finish);
      this.child.once('error', finish);
    });
    this.child.stdin.end();
    await waitForExit(1000);
    if (!this.exited) {
      this.child.kill();
      await waitForExit(2000);
    }
    this.lines?.close();
    return Boolean(this.exited);
  }
}

export function normalizeLimits(result, now = new Date()) {
  const byId = result?.rateLimitsByLimitId;
  const buckets = byId && typeof byId === 'object'
    ? Object.entries(byId)
    : result?.rateLimits ? [[result.rateLimits.limitId ?? 'unknown', result.rateLimits]] : [];
  const windows = [];
  for (const [key, bucket] of buckets) {
    for (const name of ['primary', 'secondary']) {
      const window = bucket?.[name];
      if (!window || !Number.isFinite(window.usedPercent)) continue;
      const resetSeconds = window.resetsAt;
      windows.push({
        limitId: bucket.limitId ?? key,
        window: name,
        usedPercent: window.usedPercent,
        remainingPercent: Math.max(0, Math.min(100, 100 - window.usedPercent)),
        windowDurationMins: Number.isFinite(window.windowDurationMins) ? window.windowDurationMins : null,
        resetsAt: Number.isFinite(resetSeconds) ? resetSeconds : null,
        resetsAtKorea: formatResetKorea(resetSeconds),
      });
    }
  }
  return {
    status: windows.length ? 'ok' : 'unavailable',
    source: 'documented_codex_app_server',
    observedAt: now.toISOString(),
    windows,
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const client = new UsageClient(process.argv[2] || 'codex.exe');
  try {
    await client.connect();
    console.log(JSON.stringify(await client.read(), null, 2));
  } catch (error) {
    console.log(JSON.stringify({ status: 'unavailable', attemptedAt: new Date().toISOString(), reason: error.message }));
    process.exitCode = 1;
  } finally {
    if (!(await client.close())) {
      console.error('probe_process_cleanup_unconfirmed');
      process.exitCode = 1;
    }
  }
}
