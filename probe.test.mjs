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

test('subscription plan comes only from the identified Codex rate-limit bucket', () => {
  for (const planType of ['free', 'plus', 'pro']) {
    const normalized = normalizeLimits({ rateLimitsByLimitId: {
      other: { planType: 'pro', primary: { usedPercent: 12 } },
      codex: { limitId: 'codex', planType, primary: { usedPercent: 25 } },
    } });
    assert.equal(normalized.planType, planType);
    assert.equal(normalized.windows[1].remainingPercent, 75);
  }
  assert.equal(normalizeLimits({ rateLimitsByLimitId: {
    codex: { planType: 'plus', primary: { usedPercent: 1 } },
  } }).planType, 'plus', 'the authoritative Codex map key supports an omitted optional ID');
});

test('verified protocol plan values are bounded and unknown is intentionally unavailable', () => {
  const plans = ['free', 'go', 'plus', 'pro', 'prolite', 'promax', 'team',
    'self_serve_business_prolite', 'self_serve_business_usage_based', 'business',
    'ent26', 'enterprise_cbp_automation', 'enterprise_cbp_usage_based', 'enterprise',
    'edu', 'edu_plus', 'edu_pro', 'sci', 'sci_plus'];
  for (const planType of plans) {
    assert.equal(normalizeLimits({ rateLimitsByLimitId: { codex: { planType } } }).planType, planType);
  }
  // The protocol's unknown sentinel conveys no subscription, so it is not forwarded.
  assert.equal(normalizeLimits({ rateLimitsByLimitId: { codex: { planType: 'unknown' } } }).planType, null);
});

test('missing, unknown and unrelated plan strings are not promoted to account subscription data', () => {
  for (const planType of [undefined, null, 'unknown', 'Plus', ' plus ', 'future_unrecognized_plan',
    'private-person@example.test', 'secret-access-token', 17, ['plus'], { name: 'plus' }]) {
    const normalized = normalizeLimits({ planType: 'pro', account: { planType: 'pro' }, rateLimitsByLimitId: {
      other: { planType: 'pro', primary: { usedPercent: 1 } },
      codex: { planType, primary: { usedPercent: 1 } },
    } });
    assert.equal(normalized.planType, null);
    assert.equal(JSON.stringify(normalized).includes('private-person'), false);
    assert.equal(JSON.stringify(normalized).includes('secret-access-token'), false);
  }
  assert.equal(normalizeLimits({ rateLimitsByLimitId: {
    other: { planType: 'pro', primary: { usedPercent: 1 } },
  } }).planType, null);
});

test('contradictory or duplicate Codex identities make the plan unavailable', () => {
  for (const buckets of [
    { codex: { limitId: 'other', planType: 'plus' } },
    { other: { limitId: 'codex', planType: 'plus' } },
    { codex: { planType: 'plus' }, alias: { limitId: 'codex', planType: 'pro' } },
    { codex: { planType: 'plus' }, alias: { limitId: 'codex', planType: 'plus' } },
  ]) {
    assert.equal(normalizeLimits({ rateLimitsByLimitId: buckets,
      rateLimits: { limitId: 'codex', planType: 'pro', primary: { usedPercent: 1 } },
    }).planType, null);
  }
});

test('legacy plan requires an explicit Codex ID and cannot fill a multi-bucket omission', () => {
  assert.equal(normalizeLimits({ rateLimits: { limitId: 'codex', planType: 'plus', primary: { usedPercent: 1 } } }).planType, 'plus');
  for (const limitId of [undefined, null, 'other']) {
    assert.equal(normalizeLimits({ rateLimits: { limitId, planType: 'plus', primary: { usedPercent: 1 } } }).planType, null);
  }
  for (const rateLimitsByLimitId of [{}, [], 'malformed', 17, false, { codex: { primary: { usedPercent: 1 } } }]) {
    assert.equal(normalizeLimits({ rateLimitsByLimitId,
      rateLimits: { limitId: 'codex', planType: 'pro', primary: { usedPercent: 1 } },
    }).planType, null);
  }
});

test('plan normalization never emits account identity, credentials or unrelated response fields', () => {
  const normalized = normalizeLimits({ accountId: 'private-account-id', email: 'private-person@example.test', token: 'secret-access-token',
    rateLimitsByLimitId: { codex: { limitId: 'codex', planType: 'plus', accountId: 'private-account-id',
      email: 'private-person@example.test', token: 'secret-access-token', primary: { usedPercent: 1 } } },
  }, new Date(0));
  assert.deepEqual(Object.keys(normalized).sort(), ['status', 'source', 'observedAt', 'planType', 'windows'].sort());
  assert.equal(normalized.planType, 'plus');
  assert.equal(/private-account-id|private-person|secret-access-token|email|token/.test(JSON.stringify(normalized)), false);
});
