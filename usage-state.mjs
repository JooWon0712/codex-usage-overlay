// State only: no file cache, credentials, network requests, or session-token estimates.
export const STALE_AFTER_MS = 180000;

const SAFE_REASONS = new Set([
  'process_start_failed', 'process_exited', 'process_not_running',
  'process_write_failed', 'process_read_failed', 'request_timeout',
  'usage_unavailable', 'last_update_expired', 'account_changed',
]);

export function safeReason(error) {
  const value = typeof error === 'string' ? error : error?.message;
  if (/^rpc_error_-?\d+$/.test(value || '')) return value;
  return SAFE_REASONS.has(value) ? value : 'query_failed';
}

export class UsageState {
  constructor() {
    this.state = {
      type: 'state', status: 'loading', observedAt: null,
      attemptedAt: null, reason: null, planType: null, windows: [],
    };
    this.successAtMs = null;
  }

  snapshot() {
    return { ...this.state, windows: this.state.windows.map(window => ({ ...window })) };
  }

  begin(nowMs) {
    this.state.status = 'loading';
    this.state.attemptedAt = new Date(nowMs).toISOString();
    this.state.reason = null;
    return this.snapshot();
  }

  success(normalized, nowMs) {
    if (normalized?.status !== 'ok' || !normalized.windows?.length) {
      return this.failure('usage_unavailable');
    }
    this.successAtMs = nowMs;
    this.state.status = 'ok';
    this.state.observedAt = new Date(nowMs).toISOString();
    this.state.reason = null;
    // The normalizer has already removed identifiers and unrelated account fields.
    // A fresh successful read without a plan clears the previous plan value.
    this.state.planType = normalized.planType ?? null;
    this.state.windows = normalized.windows.map(window => ({ ...window }));
    return this.snapshot();
  }

  failure(error) {
    this.state.status = this.state.observedAt ? 'stale' : 'unavailable';
    this.state.reason = safeReason(error);
    return this.snapshot();
  }

  expire(nowMs) {
    if (!['ok', 'loading'].includes(this.state.status) || this.successAtMs === null || nowMs - this.successAtMs < STALE_AFTER_MS) {
      return null;
    }
    return this.failure('last_update_expired');
  }

  invalidate(reason = 'account_changed') {
    this.successAtMs = null;
    this.state.status = 'unavailable';
    this.state.observedAt = null;
    this.state.planType = null;
    this.state.windows = [];
    this.state.reason = safeReason(reason);
    return this.snapshot();
  }
}
