import { randomUUID } from 'node:crypto';
import { closeSync, mkdirSync, openSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const MAX_INPUT_BYTES = 1024 * 1024;

function normalizeWindow(window, nowMs) {
  const usedPercentage = window?.used_percentage;
  if (!Number.isFinite(usedPercentage) || usedPercentage < 0 || usedPercentage > 100) return null;
  const reset = window.resets_at;
  const resetsAt = Number.isSafeInteger(reset) && reset >= 0 && reset <= 253402300799 ? reset : null;
  if (resetsAt !== null && resetsAt * 1000 <= nowMs) return null;
  return { usedPercentage, resetsAt };
}

export function normalizeClaudeStatusLine(input, nowMs = Date.now()) {
  const fiveHour = normalizeWindow(input?.rate_limits?.five_hour, nowMs);
  const weekly = normalizeWindow(input?.rate_limits?.seven_day, nowMs);
  if (!fiveHour && !weekly) return null;
  // This is the local time of receipt. Claude Code may reuse its last API data.
  return { observedAt: new Date(nowMs).toISOString(), fiveHour, weekly };
}

async function readSnapshot(input) {
  const chunks = [];
  let length = 0;
  for await (const chunk of input) {
    length += chunk.length;
    if (length > MAX_INPUT_BYTES) return null;
    chunks.push(chunk);
  }
  let json;
  try { json = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch (error) {
    if (error instanceof SyntaxError) return null;
    throw error;
  }
  return normalizeClaudeStatusLine(json);
}

function writeSnapshot(outputPath, snapshot) {
  const directory = path.dirname(outputPath);
  mkdirSync(directory, { recursive: true });
  const tempPath = path.join(directory, '.claude-usage-' + randomUUID() + '.tmp');
  const file = openSync(tempPath, 'wx', 0o600);
  try {
    try { writeFileSync(file, JSON.stringify(snapshot) + '\n', 'utf8'); }
    finally { closeSync(file); }
    renameSync(tempPath, outputPath);
  } finally {
    rmSync(tempPath, { force: true });
  }
}

function formatStatusLine(snapshot) {
  const parts = ['Claude Code'];
  if (snapshot.fiveHour) parts.push('5시간 ' + remaining(snapshot.fiveHour.usedPercentage));
  if (snapshot.weekly) parts.push('주간 ' + remaining(snapshot.weekly.usedPercentage));
  return parts.join(' · ');
}

function remaining(usedPercentage) {
  return Number((100 - usedPercentage).toFixed(1)) + '% 남음';
}

function isFullyQualified(value) {
  return path.isAbsolute(value) && (process.platform !== 'win32' || path.parse(value).root.length > 1);
}

async function main() {
  const [explicitPath, ...extra] = process.argv.slice(2);
  const local = process.env.LOCALAPPDATA;
  const outputPath = explicitPath ?? (local && isFullyQualified(local)
    ? path.join(local, 'CodexUsageOverlay', 'claude-usage.json')
    : '');
  if (extra.length || !isFullyQualified(outputPath)) {
    process.stderr.write('Claude Code 상태줄 기록에는 절대 파일 경로가 필요합니다.\n');
    process.exitCode = 1;
    return;
  }

  const snapshot = await readSnapshot(process.stdin);
  if (snapshot === null) {
    process.stdout.write('Claude Code · 한도 확인 대기\n');
    return;
  }
  let text = formatStatusLine(snapshot);
  try { writeSnapshot(outputPath, snapshot); }
  catch { text += ' · 오버레이 기록 실패'; }
  process.stdout.write(text + '\n');
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { await main(); }
  catch {
    process.stderr.write('Claude Code 상태줄 입력을 읽지 못했습니다.\n');
    process.exitCode = 1;
  }
}
