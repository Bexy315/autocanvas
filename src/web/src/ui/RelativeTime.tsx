import { relativeTime } from '../lib/time';

/** A point in time as "3 hours ago", with the exact time on hover and for assistive technology. */
export function RelativeTime({ value }: { value: string }) {
  const exact = new Date(value).toLocaleString();
  return (
    <time dateTime={value} title={exact}>
      {relativeTime(value)}
    </time>
  );
}
