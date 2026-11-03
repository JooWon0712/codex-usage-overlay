import { test } from 'node:test';
import assert from 'node:assert/strict';
import { normalizeLimits, UsageClient } from './probe.mjs';

// Synthetic test values, unrelated to any real account observation.

test('missing percentage is unavailable, never zero usage or full remaining', () => {
  for (const result of [null, {}, { rateLimits: { primary: {} } },
    { rateLimits: { primary: { usedPercent: null } } }]) {
    const normalized = normalizeLimits(result);
    assert.equal(normalized.status, 'unavailable');
    assert.deepEqual(normalized.windows, []);
  }
});

test('multiple account buckets and windows remain distinct from session token usage', () => {
  const normalized = normalizeLimits({
    rateLimits: { limitId: 'legacy', primary: { usedPercent: 99 } },
    rateLimitsByLimitId: {
      codex: { primary: { usedPercent: 25, windowDurationMins: 10080, resetsAt: 1700000000 } },
      other: { primary: { usedPercent: 0 }, secondary: { usedPercent: 25 } },
    },
  });
  assert.equal(normalized.status, 'ok');
  assert.deepEqual(normalized.windows.map(w => [w.limitId, w.window, w.remainingPercent]),
    [['codex', 'primary', 75], ['other', 'primary', 100], ['other', 'secondary', 75]]);
  assert.equal(normalized.windows[0].windowDurationMins, 10080);
  assert.equal(normalized.windows[0].resetsAtKorea, '2023-11-15 07:13:20 KST');
  assert.equal(normalized.windows[1].resetsAt, null);
  assert.equal(normalized.windows[1].resetsAtKorea, null);
});

test('empty multi-bucket result does not resurrect a legacy cached bucket', () => {
  const normalized = normalizeLimits({ rateLimitsByLimitId: {}, rateLimits: { primary: { usedPercent: 0 } } });
  assert.equal(normalized.status, 'unavailable');
});

test('legacy response works and untrusted reset strings are not treated as verified timestamps', () => {
  const normalized = normalizeLimits({ rateLimits: { limitId: 'codex', primary: { usedPercent: 120, resetsAt: '1700000000' } } });
  assert.equal(normalized.windows[0].remainingPercent, 0);
  assert.equal(normalized.windows[0].resetsAt, null);
  assert.equal(normalized.windows[0].resetsAtKorea, null);
});

test('client refuses to start conversations or consume earned resets', async () => {
  const client = new UsageClient();
  client.child = {};
  assert.throws(() => client.request('turn/start'), /method_not_allowed/);
  assert.throws(() => client.request('account\/rateLimitResetCredit\/consume'), /method_not_allowed/);
  client.child = null;
  await assert.rejects(client.request('account/rateLimits/read'), /process_not_running/);
});
