import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, rmdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { normalizeClaudeStatusLine } from './claude-statusline.mjs';

const SCRIPT = fileURLToPath(new URL('./claude-statusline.mjs', import.meta.url));
const NOW = Date.UTC(2026, 10, 17, 1);
const RESET = NOW / 1000 + 3600;

test('normalizes only the documented subscription windows', () => {
  const input = {
    session_id: 'synthetic-session',
    transcript_path: 'synthetic-path',
    oauth_token: 'synthetic-token',
    context_window: { used_percentage: 99 },
    rate_limits: {
      five_hour: { used_percentage: 23.5, resets_at: RESET, extra: 'ignored' },
      seven_day: { used_percentage: 100, resets_at: RESET + 7 * 86400 },
      spend_limit: { used_percentage: 95, used_usd: 100 },
    },
  };
  assert.deepEqual(normalizeClaudeStatusLine(input, NOW), {
    observedAt: new Date(NOW).toISOString(),
    fiveHour: { usedPercentage: 23.5, resetsAt: RESET },
    weekly: { usedPercentage: 100, resetsAt: RESET + 7 * 86400 },
  });
});

test('missing, invalid and expired windows are evaluated independently', () => {
  assert.deepEqual(normalizeClaudeStatusLine({ rate_limits: {
    five_hour: { used_percentage: 12, resets_at: NOW / 1000 },
    seven_day: { used_percentage: 0, resets_at: 'invalid' },
  } }, NOW), {
    observedAt: new Date(NOW).toISOString(),
    fiveHour: null,
    weekly: { usedPercentage: 0, resetsAt: null },
  });

  for (const input of [null, {}, { rate_limits: {} },
    { rate_limits: { spend_limit: { used_percentage: 20 } } },
    { rate_limits: { five_hour: { used_percentage: 1, resets_at: NOW / 1000 - 1 } } },
    { rate_limits: { five_hour: { used_percentage: '1' }, seven_day: { used_percentage: -1 } } },
    { rate_limits: { five_hour: { used_percentage: 101 }, seven_day: { used_percentage: null } } }]) {
    assert.equal(normalizeClaudeStatusLine(input, NOW), null);
  }
});

function callback(input, localAppData, args = []) {
  const result = spawnSync(process.execPath, [SCRIPT, ...args], {
    input,
    encoding: 'utf8',
    windowsHide: true,
    timeout: 5000,
    env: { ...process.env, LOCALAPPDATA: localAppData },
  });
  assert.ifError(result.error);
  return result;
}

test('records filtered snapshots and preserves them during empty or malformed callbacks', () => {
  const scratch = mkdtempSync(path.join(tmpdir(), 'cuo-claude-statusline-'));
  const directory = path.join(scratch, 'CodexUsageOverlay');
  const snapshot = path.join(directory, 'claude-usage.json');
  try {
    const initial = callback(JSON.stringify({ rate_limits: {
      five_hour: { used_percentage: 23.5 },
    }, transcript_path: 'synthetic-path' }), scratch);
    assert.equal(initial.status, 0);
    assert.equal(initial.stdout.trim(), 'Claude Code · 5시간 76.5% 남음');
    assert.equal(initial.stderr, '');

    const original = readFileSync(snapshot, 'utf8');
    const saved = JSON.parse(original);
    assert.deepEqual(Object.keys(saved), ['observedAt', 'fiveHour', 'weekly']);
    assert.ok(Number.isFinite(Date.parse(saved.observedAt)));
    assert.deepEqual(saved.fiveHour, { usedPercentage: 23.5, resetsAt: null });
    assert.equal(saved.weekly, null);

    for (const input of ['', '{}', '{synthetic-malformed-input',
      JSON.stringify({ rate_limits: { five_hour: { used_percentage: 2, resets_at: 1 } } })]) {
      const result = callback(input, scratch);
      assert.equal(result.status, 0);
      assert.equal(result.stdout.trim(), 'Claude Code · 한도 확인 대기');
      assert.equal(result.stderr, '');
      assert.equal(readFileSync(snapshot, 'utf8'), original);
    }

    const result = callback(JSON.stringify({ rate_limits: {
      seven_day: { used_percentage: 0 },
    } }), scratch);
    assert.equal(result.status, 0);
    assert.equal(result.stdout.trim(), 'Claude Code · 주간 100% 남음');
    const updated = JSON.parse(readFileSync(snapshot, 'utf8'));
    assert.equal(updated.fiveHour, null);
    assert.deepEqual(updated.weekly, { usedPercentage: 0, resetsAt: null });
    assert.deepEqual(readdirSync(directory), ['claude-usage.json']);
  } finally {
    rmSync(snapshot, { force: true });
    if (readdirSync(scratch).includes('CodexUsageOverlay')) rmdirSync(directory);
    rmdirSync(scratch);
  }
});

test('accepts an explicit absolute output path and keeps the status line usable on write failure', () => {
  const scratch = mkdtempSync(path.join(tmpdir(), 'cuo-claude-statusline-'));
  const alternate = path.join(scratch, 'alternate.json');
  const invalidTarget = path.join(scratch, 'existing-directory');
  mkdirSync(invalidTarget);
  try {
    const input = JSON.stringify({ rate_limits: { five_hour: { used_percentage: 33 } } });
    const rejected = callback(input, scratch, ['relative.json']);
    assert.equal(rejected.status, 1);

    const result = callback(input, scratch, [alternate]);
    assert.equal(result.status, 0);
    assert.equal(JSON.parse(readFileSync(alternate, 'utf8')).fiveHour.usedPercentage, 33);

    const failed = callback(input, scratch, [invalidTarget]);
    assert.equal(failed.status, 0);
    assert.equal(failed.stdout.trim(), 'Claude Code · 5시간 67% 남음 · 오버레이 기록 실패');
    assert.equal(failed.stderr, '');
    assert.deepEqual(readdirSync(scratch).sort(), ['alternate.json', 'existing-directory']);
  } finally {
    rmSync(alternate, { force: true });
    rmdirSync(invalidTarget);
    rmdirSync(scratch);
  }
});
