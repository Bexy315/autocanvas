/**
 * Per-browser preferences. Storage can be missing or blocked (private windows, strict settings);
 * then the defaults apply and nothing fails.
 */
export function readSetting(key: string): string | null {
  try {
    return globalThis.localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function writeSetting(key: string, value: string): void {
  try {
    globalThis.localStorage.setItem(key, value);
  } catch {
    // Not remembered, but the choice still applies to this page.
  }
}
