import type { SessionRateLimit } from 'claude-code'
import { expect, mock, test } from 'claude-code/testing'

const receivedAt = Date.parse('2026-11-18T00:00:00Z')
const localAppData = 'C:\\fixture\\Local\\'
const snapshotPath = 'C:/fixture/Local/CodexUsageOverlay/claude-usage.json'
const session = { cwd: 'C:/fixture', surface: 'terminal', isInteractive: true } as const
const fiveHour = { kind: 'five_hour', percentUsed: 0, resetsAt: '2026-11-18T04:00:00Z' }
const weekly = { kind: 'seven_day', percentUsed: 42.5 }
const expired = { kind: 'five_hour', percentUsed: 20, resetsAt: '2026-11-18T00:00:00Z' }
const invalid = { kind: 'seven_day', percentUsed: 101 }
const perModel = { kind: 'seven_day_opus', percentUsed: 22 }
const snapshot = {
  observedAt: '2026-11-18T00:00:00.000Z',
  fiveHour: { usedPercentage: 0, resetsAt: Date.parse(fiveHour.resetsAt) / 1000 },
  weekly: { usedPercentage: 42.5, resetsAt: null },
}

const startupCases: { name: string; limits: SessionRateLimit[]; path: string; expected: unknown }[] = [
  { name: 'shares only the two supported limits', limits: [fiveHour, weekly, perModel], path: localAppData, expected: snapshot },
  { name: 'shares the weekly limit when the five-hour limit has expired', limits: [expired, weekly], path: localAppData, expected: { ...snapshot, fiveHour: null } },
  { name: 'leaves the previous snapshot intact with no valid limits', limits: [expired, invalid, perModel], path: localAppData, expected: null },
  { name: 'skips a relative data folder', limits: [fiveHour], path: 'relative/path', expected: null },
]

for (const { name, limits, path, expected } of startupCases) {
  test(`session start ${name}`, async ($, on) => {
    const writes: { path: string; data: unknown }[] = []
    mock.clock(on, { now: receivedAt })
    mock.env(on, { LOCALAPPDATA: path })
    on('session.usage', () => ({
      value: { startedAt: receivedAt, context: { tokens: 123, window: 1000 }, cost: { usd: 19 }, rateLimits: limits },
    }))
    on('fs.write', (_, event) => {
      writes.push({ path: event.path.replace(/\\/g, '/'), data: JSON.parse(event.text) })
      return { value: undefined }
    })
    on('session.start', () => ({ cwd: session.cwd }))

    expect(await $.session.start(session)).toEqual({ cwd: session.cwd })
    expect(writes).toEqual(expected === null ? [] : [{ path: snapshotPath, data: expected }])
  })
}

test('a refused write still passes the startup event to the next hook', async ($, on) => {
  let writeAttempts = 0
  mock.clock(on, { now: receivedAt })
  mock.env(on, { LOCALAPPDATA: localAppData })
  on('session.usage', () => ({
    value: { startedAt: receivedAt, context: { window: 1000 }, rateLimits: [fiveHour] },
  }))
  on('fs.write', () => {
    writeAttempts++
    return { deny: 'fixture denial' }
  })
  on('session.start', () => ({ cwd: session.cwd }))

  expect(await $.session.start(session)).toEqual({ cwd: session.cwd })
  expect(writeAttempts).toBe(1)
})

test('measurement writes only when the rate limits change', async ($, on) => {
  const writes: unknown[] = []
  mock.clock(on, { now: receivedAt })
  mock.env(on, { LOCALAPPDATA: localAppData })
  on('fs.write', (_, event) => {
    writes.push(JSON.parse(event.text))
    return { value: undefined }
  })
  on('session.measure', (_, event) => event)

  await $.session.measure({ changed: [], rateLimits: [fiveHour, weekly] })
  expect(writes).toEqual([])
  await $.session.measure({ changed: ['rateLimits'], rateLimits: [fiveHour, weekly] })
  expect(writes).toEqual([snapshot])
})
