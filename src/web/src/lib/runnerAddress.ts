/**
 * The runner endpoint as a runner would most likely reach it, when the instance settings name none:
 * this host, the default runner port.
 */
export function guessRunnerAddress(): string {
  return `${globalThis.location.protocol}//${globalThis.location.hostname}:5081`;
}
