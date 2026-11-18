import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { collectUsage, makeProbeEnvironment, makeSnapshot, readUsageOutput, resolveClaude } from './claude-usage-probe.mjs';

const script = fileURLToPath(new URL('./claude-usage-probe.mjs', import.meta.url));
const now = Date.parse('2026-11-18T08:00:00Z');
const reset = '2026-11-18T19:00:00.12345+09:00';
const observedAt = new Date(now).toISOString();
const result = {
  type: 'result', subtype: 'success', is_error: false, local_command: 'usage', num_turns: 0, duration_api_ms: 0,
  usage: { input_tokens: 0, output_tokens: 0 }, modelUsage: {},
};
const fiveHour = { kind: 'session', group: 'session', percent: 2, resets_at: reset, is_active: false };
const weekly = { kind: 'weekly_all', group: 'weekly', percent: 9, resets_at: '2026-11-22T17:00:00+09:00', is_active: true, scope: null };

function output(rows, overrides = {}) {
  return [
    { type: 'system', subtype: 'init' },
    { type: 'assistant', usage_report: { rate_limits: { limits: rows } } },
    { ...result, ...overrides },
  ].map(event => JSON.stringify(event)).join('\n') + '\n';
}

test('reads official unscoped subscription periods and keeps an inactive row with a reported value', () => {
  const scoped = { ...weekly, percent: 95, scope: { model: { display_name: 'Foo' } } };
  assert.deepEqual(readUsageOutput(output([fiveHour, weekly, scoped]), now), {
    fiveHour: { usedPercentage: 2, resetsAt: Math.floor(Date.parse(reset) / 1000), observedAt },
    weekly: { usedPercentage: 9, resetsAt: Date.parse(weekly.resets_at) / 1000, observedAt },
  });
});

test('does not trust a report attached to a failed, non-local, or model-bearing run', () => {
  for (const override of [
    { is_error: true }, { local_command: null }, { num_turns: 1 }, { duration_api_ms: 2 },
    { usage: { input_tokens: 2, output_tokens: 0 } }, { usage: { input_tokens: 0, output_tokens: 3 } },
    { modelUsage: { sample: { inputTokens: 3 } } },
  ]) assert.equal(readUsageOutput(output([fiveHour], override), now), null);
  assert.equal(readUsageOutput('a line that was not a JSON event\n' + output([fiveHour]), now), null);
  assert.equal(readUsageOutput(output([]), now), null);
  assert.equal(readUsageOutput(output(null), now), null);
});

test('keeps a valid period when another is missing, expired, invalid, or ambiguous', () => {
  const expected = { fiveHour: null, weekly: { usedPercentage: 9, resetsAt: Date.parse(weekly.resets_at) / 1000, observedAt } };
  for (const invalid of [
    [], [{ ...fiveHour, resets_at: '2026-11-18T07:00:00Z' }], [{ ...fiveHour, resets_at: '2026-11-18T19:00:00' }],
    [{ ...fiveHour, percent: 101 }], [fiveHour, { ...fiveHour, percent: 4 }], [{ ...fiveHour, scope: {} }],
  ]) assert.deepEqual(readUsageOutput(output([...invalid, weekly]), now), expected);
  assert.deepEqual(readUsageOutput(output([{ ...fiveHour, resets_at: null }]), now).fiveHour,
    { usedPercentage: 2, resetsAt: null, observedAt });
});

test('partial and failed queries preserve only a current row from the same confirmed account', () => {
  const first = readUsageOutput(output([fiveHour, weekly]), now);
  const prior = { ...first, accountKey: 'same', subscriptionType: 'max' };
  const fresh = readUsageOutput(output([weekly]), now + 1000);
  const partial = makeSnapshot({ fresh, previous: prior, accountKey: 'same', status: 'partial', attemptedMs: now + 1000, nowMs: now + 1000 });
  assert.equal(partial.attemptedAt, new Date(now + 1000).toISOString());
  assert.equal(partial.status, 'partial');
  assert.deepEqual(partial.fiveHour, first.fiveHour);
  assert.deepEqual(partial.weekly, fresh.weekly);
  assert.equal(Object.hasOwn(partial, 'subscriptionType'), false);
  for (const key of ['changed', null]) {
    const other = makeSnapshot({ previous: prior, accountKey: key, status: 'unavailable', attemptedMs: now, nowMs: now });
    assert.equal(other.fiveHour, null);
    assert.equal(other.weekly, null);
    assert.equal(Object.hasOwn(other, 'subscriptionType'), false);
  }
  const later = Date.parse('2026-11-18T12:00:00Z');
  const failed = makeSnapshot({ previous: prior, accountKey: 'same', status: 'unavailable', attemptedMs: later, nowMs: later });
  assert.equal(failed.fiveHour, null);
  assert.deepEqual(failed.weekly, first.weekly);
  assert.equal(Object.hasOwn(failed, 'subscriptionType'), false);
});

test('snapshots store a recognized current plan independently of quota availability', () => {
  const fresh = readUsageOutput(output([weekly]), now);
  const current = { fresh, accountKey: 'same', subscriptionType: ' Pro ', status: 'partial', attemptedMs: now, nowMs: now };
  assert.equal(makeSnapshot(current).subscriptionType, 'pro');
  assert.equal(makeSnapshot({ ...current, status: 'ok' }).subscriptionType, 'pro');
  const unavailable = makeSnapshot({ ...current, fresh: null, status: 'unavailable' });
  assert.equal(unavailable.subscriptionType, 'pro');
  assert.equal(unavailable.fiveHour, null);
  assert.equal(unavailable.weekly, null);
  for (const invalid of [
    { accountKey: null }, { status: 'auth-required' }, { status: 'missing-cli' },
    { subscriptionType: 'unknown' }, { subscriptionType: { raw: 'max' } }, { subscriptionType: 'pro trial' },
  ]) assert.equal(Object.hasOwn(makeSnapshot({ ...current, ...invalid }), 'subscriptionType'), false);
});

test('probe removes credentials and provider overrides only from its child environment', () => {
  const env = { Path: 'example', anthropic_api_key: 'sample', ANTHROPIC_BASE_URL: 'example',
    CLAUDE_CODE_OAUTH_TOKEN: 'opaque', CLAUDE_CODE_USE_BEDROCK: '1', CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC: '1' };
  assert.deepEqual(makeProbeEnvironment(env), { Path: 'example', CLAUDE_CODE_DISABLE_AUTO_MEMORY: '1' });
  assert.equal(env.anthropic_api_key, 'sample');
  assert.equal(resolveClaude({ CODEX_USAGE_OVERLAY_CLAUDE: 'relative.exe' }), null);
  assert.equal(resolveClaude({ CODEX_USAGE_OVERLAY_CLAUDE: process.execPath }), process.execPath);
});

test('probe only issues the official /usage command after checking the version and native login', async () => {
  const ok = stdout => ({ code: 0, stdout });
  const version = ok('2.1.326 (Claude Code)\n');
  const login = (email, subscriptionType) => ok(JSON.stringify({ loggedIn: true, authMethod: 'claude.ai', email, orgId: 'fixture', subscriptionType }));
  const report = ok(output([fiveHour, weekly]));
  const first = login('sample-one@example.invalid');
  const schedule = async (...responses) => {
    const calls = [];
    const status = await collectUsage('synthetic launcher', {}, now + 3000, (_, args) => {
      calls.push(args);
      assert.ok(responses.length > 0);
      return Promise.resolve(responses.shift());
    }, () => now);
    assert.equal(responses.length, 0);
    return { status, calls };
  };

  const compatible = await schedule(version, first, report, first);
  assert.equal(compatible.status.status, 'ok');
  assert.match(compatible.status.accountKey, /^[a-f0-9]{64}$/);
  assert.equal(compatible.status.fresh.fiveHour.usedPercentage, 2);
  assert.equal(compatible.calls.filter(args => args.includes('/usage')).length, 1);
  assert.deepEqual(compatible.calls[2].slice(-2), ['--', '/usage']);
  assert.equal(Object.hasOwn(compatible.status, 'subscriptionType'), false);

  const pro = login('sample-one@example.invalid', 'pro');
  const max = login('sample-one@example.invalid', 'max');
  const withPlan = await schedule(version, login('sample-one@example.invalid', '  PRO '), report, pro);
  assert.equal(withPlan.status.subscriptionType, 'pro');
  assert.deepEqual(withPlan.calls, compatible.calls);
  const partial = await schedule(version, max, ok(output([weekly])), max);
  assert.equal(partial.status.status, 'partial');
  assert.equal(partial.status.subscriptionType, 'max');
  const unavailable = await schedule(version, pro, ok(output([])), pro);
  assert.equal(unavailable.status.status, 'unavailable');
  assert.equal(unavailable.status.fresh, null);
  assert.equal(unavailable.status.subscriptionType, 'pro');

  const noIdentity = login(undefined, 'max');
  const unknownPlan = login('sample-one@example.invalid', 'unknown-plan');
  for (const [before, usage, after] of [
    [pro, report, max], [pro, report, first], [noIdentity, report, noIdentity], [unknownPlan, report, unknownPlan],
    [{ ...pro, code: 1 }, report, pro], [pro, report, { ...pro, code: 1 }],
    [pro, ok(output([])), ok('{"loggedIn":false,"authMethod":"none"}')],
    [pro, ok(output([])), ok('')],
  ]) {
    const { status } = await schedule(version, before, usage, after);
    assert.equal(Object.hasOwn(status, 'subscriptionType'), false);
  }

  for (const old of ['2.1.272 (Claude Code)\n', 'unexpected output']) {
    const unsupported = await schedule(ok(old));
    assert.equal(unsupported.status.status, 'unavailable');
    assert.deepEqual(unsupported.calls, [['--version']]);
  }
  const signedOut = await schedule(version, ok('{"loggedIn":false,"authMethod":"none"}'));
  assert.equal(signedOut.status.status, 'auth-required');
  assert.equal(signedOut.calls.length, 2);
  const accountChanged = await schedule(version, pro, report, login('sample-two@example.invalid', 'pro'));
  assert.deepEqual(accountChanged.status, { status: 'unavailable' });
  const authError = '{"type":"assistant","error":"authentication_failed"}\n';
  const expired = await schedule(version, pro, { code: 1, stdout: authError });
  assert.deepEqual(expired.status, { status: 'auth-required' });
  const conflicting = await schedule(version, pro, ok(authError + report.stdout));
  assert.deepEqual(conflicting.status, { status: 'auth-required' });
});

test('scheduler requires start signal before writing, and missing CLI returns a failure snapshot', t => {
  const local = mkdtempSync(path.join(tmpdir(), 'claude-usage-probe-'));
  t.after(() => rmSync(local, { recursive: true, force: true }));
  const env = { ...process.env, LOCALAPPDATA: local, CODEX_USAGE_OVERLAY_CLAUDE: path.join(local, 'missing.exe') };
  const snapshot = path.join(local, 'CodexUsageOverlay', 'claude-usage-auto.json');
  const refused = spawnSync(process.execPath, [script, '--wait-for-start'], { env, input: 'deny\n', windowsHide: true });
  assert.equal(refused.status, 1);
  assert.equal(existsSync(snapshot), false);
  const accepted = spawnSync(process.execPath, [script, '--wait-for-start'], { env, input: '\uFEFFstart\r\n', windowsHide: true });
  assert.equal(accepted.status, 1);
  const state = JSON.parse(readFileSync(snapshot, 'utf8'));
  assert.equal(state.status, 'missing-cli');
  assert.equal(state.fiveHour, null);
  assert.equal(state.weekly, null);
  assert.ok(Number.isFinite(Date.parse(state.attemptedAt)));
});

test('print wrapper forwards arguments, output, and exit code and writes only a completion marker', t => {
  const local = mkdtempSync(path.join(tmpdir(), 'claude-usage-wrapper-'));
  t.after(() => rmSync(local, { recursive: true, force: true }));
  const env = { ...process.env, LOCALAPPDATA: local, CODEX_USAGE_OVERLAY_CLAUDE: process.execPath };
  const child = spawnSync(process.execPath, [script, '--run', '-p', 'process.exitCode=7; "synthetic output"'],
    { env, encoding: 'utf8', windowsHide: true });
  assert.equal(child.status, 7);
  assert.equal(child.stdout.trim(), 'synthetic output');
  const directory = path.join(local, 'CodexUsageOverlay');
  assert.equal(existsSync(path.join(directory, 'claude-usage-auto.json')), false);
  const marker = JSON.parse(readFileSync(path.join(directory, 'claude-refresh-request.json'), 'utf8'));
  assert.deepEqual(Object.keys(marker), ['requestedAt']);
  assert.ok(Number.isFinite(Date.parse(marker.requestedAt)));
});
