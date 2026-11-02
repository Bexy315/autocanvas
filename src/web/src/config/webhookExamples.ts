/**
 * Example calls of a webhook address, as the dialog after creating one shows them. A signed call
 * sends `X-AutoCanvas-Signature: sha256=` and the hex HMAC-SHA256 of the exact body, keyed with the
 * signing secret (docs/user/triggers.md).
 */

const body = '{"reason":"a test call"}';

/** A shell string in single quotes, with any single quote in it closed and escaped. */
const quoted = (text: string) => `'${text.replaceAll("'", `'\\''`)}'`;

/** A PowerShell string in single quotes, where a single quote is doubled. */
const psQuoted = (text: string) => `'${text.replaceAll("'", "''")}'`;

export function curlExample(url: string, secret: string | null): string {
  if (secret === null) {
    return [`curl -X POST ${quoted(url)} \\`, `  -H 'Content-Type: application/json' \\`, `  -d ${quoted(body)}`].join(
      '\n',
    );
  }
  return [
    `body=${quoted(body)}`,
    `signature="sha256=$(printf '%s' "$body" | openssl dgst -sha256 -hmac ${quoted(secret)} -hex | cut -d' ' -f2)"`,
    `curl -X POST ${quoted(url)} \\`,
    `  -H 'Content-Type: application/json' \\`,
    `  -H "X-AutoCanvas-Signature: $signature" \\`,
    `  -d "$body"`,
  ].join('\n');
}

export function powerShellExample(url: string, secret: string | null): string {
  const call = (headers: string) =>
    `Invoke-RestMethod -Method Post -Uri ${psQuoted(url)} -ContentType 'application/json'${headers} -Body $body`;
  if (secret === null) {
    return [`$body = ${psQuoted(body)}`, call('')].join('\n');
  }
  return [
    `$body = ${psQuoted(body)}`,
    `$hmac = [System.Security.Cryptography.HMACSHA256]::new([Text.Encoding]::UTF8.GetBytes(${psQuoted(secret)}))`,
    `$signature = 'sha256=' + [Convert]::ToHexString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($body))).ToLowerInvariant()`,
    call(" -Headers @{ 'X-AutoCanvas-Signature' = $signature }"),
  ].join('\n');
}
