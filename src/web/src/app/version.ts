/** `1.0.0+9dad45aec10d…` (version and source revision) shown as `1.0.0 (9dad45a)`. */
export function shortVersion(version: string): string {
  const [release, revision] = version.split('+', 2);
  return revision ? `${release} (${revision.slice(0, 7)})` : version;
}
