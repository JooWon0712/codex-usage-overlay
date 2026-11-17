function readWindow(limits, kind, receivedAt) {
  const window = limits.find(limit => limit.kind === kind)
  if (!window || !Number.isFinite(window.percentUsed) || window.percentUsed < 0 || window.percentUsed > 100) {
    return null
  }

  const resetTime = Date.parse(window.resetsAt)
  const resetsAt = Number.isFinite(resetTime) ? Math.floor(resetTime / 1000) : null
  if (resetsAt !== null && resetsAt * 1000 <= receivedAt) return null
  return { usedPercentage: window.percentUsed, resetsAt }
}

async function publishUsage($, limits) {
  if (limits.length === 0) return

  const receivedAt = await $.clock.now()
  const fiveHour = readWindow(limits, 'five_hour', receivedAt)
  const weekly = readWindow(limits, 'seven_day', receivedAt)
  if (!fiveHour && !weekly) return

  const localAppData = await $.env.get('LOCALAPPDATA')
  if (!localAppData || !/^[A-Za-z]:[\\/]/.test(localAppData)) return

  // The engine may already know the limits at startup. This is local receipt time.
  const snapshot = { observedAt: new Date(receivedAt).toISOString(), fiveHour, weekly }
  const directory = localAppData.replace(/[\\/]+$/, '')
  await $.fs.write(`${directory}/CodexUsageOverlay/claude-usage.json`, JSON.stringify(snapshot) + '\n')
}

export function register(on) {
  on('session.start', async ($, event, next) => {
    try {
      const usage = await $.session.usage()
      await publishUsage($, usage.rateLimits)
    } catch {
      // Failure to share optional usage data must not change the session event.
    }
    return next(event)
  })

  on('session.measure', async ($, event, next) => {
    if (event.changed.includes('rateLimits')) {
      try {
        await publishUsage($, event.rateLimits)
      } catch {
        // Leave the event intact even if the snapshot cannot be written.
      }
    }
    return next(event)
  })
}
