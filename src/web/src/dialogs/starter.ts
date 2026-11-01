/**
 * The file a new automation starts with when none is uploaded: started by hand, one job with one
 * step, runnable on the reference runner. Small on purpose; the editor is where it grows.
 */
export function starterFile(name: string): string {
  const title = name.trim() || 'New automation';
  return `version: 1
name: ${JSON.stringify(title)}
description: Started from the AutoCanvas starter file.

triggers:
  manual: true

jobs:
  hello:
    runs_on: linux
    image: alpine:3.22
    steps:
      - run: echo "Hello from AutoCanvas"
`;
}
