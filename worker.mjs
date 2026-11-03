import { createInterface } from 'node:readline';
import { pathToFileURL } from 'node:url';
import { UsageClient, normalizeLimits } from './probe.mjs';
import { UsageState } from './usage-state.mjs';

export const POLL_INTERVAL_MS = 60000;
export const MANUAL_MIN_INTERVAL_MS = 5000;

export class UsageWorker {
  constructor({
    executable = 'codex.exe',
    createClient = () => new UsageClient(executable),
    now = () => Date.now(),
    emit = value => process.stdout.write(JSON.stringify(value) + '\n'),
    parentPid = null,
    parentExists = pid => {
      try { process.kill(pid, 0); return true; }
      catch (error) { return error.code === 'EPERM'; }
    },
    onParentLost = null,
  } = {}) {
    this.createClient = createClient;
    this.now = now;
    this.emit = emit;
    this.parentPid = parentPid;
    this.parentExists = parentExists;
    this.onParentLost = onParentLost;
    this.state = new UsageState();
    this.active = false;
    this.stopping = false;
    this.lastAttemptMs = null;
    this.lastParentCheckMs = null;
    this.generation = 0;
    this.lastAccountId = null;
    this.client = null;
    this.inFlight = null;
    this.clientClosing = null;
  }

  start({ timers = true } = {}) {
    this.publish(this.state.snapshot());
    if (timers) this.timer = setInterval(() => { void this.tick(); }, 1000);
  }

  publish(snapshot) {
    if (!this.stopping) this.emit(snapshot);
  }

  setActive(value) {
    if (this.stopping) return Promise.resolve(false);
    this.active = value === true;
    return this.active ? this.refresh(false) : Promise.resolve(false);
  }

  async tick() {
    if (this.stopping) return;
    const nowMs = this.now();
    if (this.parentPid && (this.lastParentCheckMs === null || nowMs - this.lastParentCheckMs >= 5000)) {
      this.lastParentCheckMs = nowMs;
      if (!this.parentExists(this.parentPid)) {
        if (this.onParentLost) this.onParentLost();
        else await this.stop();
        return;
      }
    }
    const expired = this.state.expire(nowMs);
    if (expired) this.publish(expired);
    if (this.active) await this.refresh(false);
  }

  refresh(manual = true) {
    if (this.stopping) return Promise.resolve(false);
    if (this.inFlight) return this.inFlight;
    const nowMs = this.now();
    const interval = manual ? MANUAL_MIN_INTERVAL_MS : POLL_INTERVAL_MS;
    if (this.lastAttemptMs !== null && nowMs - this.lastAttemptMs < interval) return Promise.resolve(false);
    this.lastAttemptMs = nowMs;
    this.publish(this.state.begin(nowMs));
    this.inFlight = this.query().finally(() => { this.inFlight = null; });
    return this.inFlight;
  }

  async query() {
    try {
      if (!this.client || this.client.exited) {
        await this.dropClient();
        if (this.stopping) return false;
        const client = this.createClient();
        this.client = client;
        await client.connect();
        if (this.stopping) return false;
        // This connection's auth notifications can invalidate cached observations.
        // They do not guarantee immediate detection of sign-in changes in a separate desktop process.
        client.lines?.on('line', line => this.handleNotification(line));
      }
      const generation = this.generation;
      const result = await this.client.request('account/rateLimits/read');
      if (this.stopping || generation !== this.generation) return false;
      const accountId = typeof result?.accountId === 'string' ? result.accountId : null;
      if (accountId && this.lastAccountId && accountId !== this.lastAccountId) {
        this.state.invalidate('account_changed');
      }
      this.lastAccountId = accountId;
      this.publish(this.state.success(normalizeLimits(result), this.now()));
      return true;
    } catch (error) {
      if (!this.stopping) this.publish(this.state.failure(error));
      // A fresh connection on the next permitted attempt prevents timed-out responses
      // and dead transports from being reused as successful reads.
      await this.dropClient();
      return false;
    }
  }

  handleNotification(line) {
    if (this.stopping || line.length > 65536) return;
    let message;
    try { message = JSON.parse(line); } catch { return; }
    if (!message || typeof message !== 'object' || Array.isArray(message)) return;
    if (message.method !== 'account/updated') return;
    this.generation++;
    this.lastAccountId = null;
    this.lastAttemptMs = null;
    this.publish(this.state.invalidate('account_changed'));
  }

  async dropClient() {
    if (this.clientClosing) return this.clientClosing;
    const client = this.client;
    this.client = null;
    if (!client) return true;
    this.clientClosing = Promise.resolve(client.close()).catch(() => false);
    try { return await this.clientClosing; }
    finally { this.clientClosing = null; }
  }

  async stop() {
    if (this.stopPromise) return this.stopPromise;
    this.stopping = true;
    this.active = false;
    clearInterval(this.timer);
    this.stopPromise = (async () => {
      const closed = await this.dropClient();
      await this.inFlight;
      return closed;
    })();
    return this.stopPromise;
  }
}

export function parseArguments(args) {
  let executable = 'codex.exe';
  let parentPid = null;
  for (let index = 0; index < args.length; index++) {
    if (args[index] === '--codex' && args[index + 1]) executable = args[++index];
    else if (args[index] === '--parent' && /^\d+$/.test(args[index + 1] || '')) {
      parentPid = Number(args[++index]);
      if (!Number.isSafeInteger(parentPid) || parentPid <= 0) throw new Error('invalid_arguments');
    } else throw new Error('invalid_arguments');
  }
  return { executable, parentPid };
}

async function main() {
  let options;
  try { options = parseArguments(process.argv.slice(2)); }
  catch {
    process.stdout.write(JSON.stringify({ type: 'state', status: 'unavailable', observedAt: null,
      attemptedAt: null, reason: 'invalid_arguments', windows: [] }) + '\n');
    process.exitCode = 1;
    return;
  }
  let input;
  let ending = false;
  const finish = async () => {
    if (ending) return;
    ending = true;
    input?.close();
    const stopped = await worker.stop();
    process.exit(stopped ? 0 : 1);
  };
  const worker = new UsageWorker({ ...options, onParentLost: () => { void finish(); } });
  process.stdout.on('error', () => { void finish(); });
  process.stdin.on('error', () => { void finish(); });
  process.on('SIGINT', () => { void finish(); });
  process.on('SIGTERM', () => { void finish(); });
  input = createInterface({ input: process.stdin, crlfDelay: Infinity });
  input.on('line', line => {
    if (line.length > 2048 || ending) return;
    let message;
    try { message = JSON.parse(line); } catch { return; }
    if (!message || typeof message !== 'object' || Array.isArray(message)) return;
    if (message.command === 'active') void worker.setActive(message.value);
    else if (message.command === 'refresh') void worker.refresh(true);
    else if (message.command === 'quit') void finish();
  });
  input.on('close', () => { void finish(); });
  worker.start();
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await main();
