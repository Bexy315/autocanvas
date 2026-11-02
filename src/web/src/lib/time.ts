const units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
];

/** "3 hours ago" or "in 2 days"; under a minute is "just now". */
export function relativeTime(value: string | Date, now: Date = new Date()): string {
  const seconds = Math.round((new Date(value).getTime() - now.getTime()) / 1000);
  if (Math.abs(seconds) < 60) {
    return 'just now';
  }
  // English like every other text of the UI; exact dates use the browser's format (RelativeTime's title).
  const format = new Intl.RelativeTimeFormat('en', { numeric: 'auto' });
  for (const [unit, size] of units) {
    if (Math.abs(seconds) >= size) {
      return format.format(Math.round(seconds / size), unit);
    }
  }
  return format.format(Math.trunc(seconds / 60), 'minute');
}

/** How long something took: `850 ms`, `12 s`, `3 min 4 s`, `1 h 2 min`; up to now while it runs. */
export function duration(from: string, to?: string | null, now: Date = new Date()): string {
  const milliseconds = Math.max(0, (to ? new Date(to) : now).getTime() - new Date(from).getTime());
  if (milliseconds < 1000) {
    return `${milliseconds} ms`;
  }
  const seconds = Math.floor(milliseconds / 1000);
  if (seconds < 60) {
    return `${seconds} s`;
  }
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return seconds % 60 === 0 ? `${minutes} min` : `${minutes} min ${seconds % 60} s`;
  }
  const hours = Math.floor(minutes / 60);
  return minutes % 60 === 0 ? `${hours} h` : `${hours} h ${minutes % 60} min`;
}
