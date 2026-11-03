import { useState } from 'react';
import type { CreatedWebhook } from '../api/configuration';
import { CopyButton, CopyValue, OnceDialog } from '../ui/SecretOnce';
import { curlExample, powerShellExample } from './webhookExamples';

/**
 * The new webhook address and signing secret, shown this one time, with example calls. It closes
 * only once the user says they saved them, so Esc or a stray click cannot lose them.
 */
export function WebhookCreatedDialog({ created, onClose }: { created: CreatedWebhook; onClose: () => void }) {
  const [shell, setShell] = useState<'curl' | 'powershell'>('curl');
  const url = created.url ?? `${globalThis.location.origin}${created.path}`;
  const secret = created.signingSecret ?? null;
  const example = shell === 'curl' ? curlExample(url, secret) : powerShellExample(url, secret);

  return (
    <OnceDialog
      wide
      title="Webhook address created"
      description="Calls of this address start a run; with a signature only when they are signed."
      saveLabel={`I have saved the address${secret ? ' and the secret' : ''}`}
      onClose={onClose}
    >
      {!created.enabled && (
        <p className="text-sm text-muted">
          Calls start runs once the automation's file has <code className="font-mono">webhook: true</code> under
          triggers.
        </p>
      )}
      <CopyValue label="Address" value={url} />
      {secret && <CopyValue label="Signing secret" value={secret} />}
      <div className="flex flex-col gap-1">
        <div className="flex items-center justify-between">
          <fieldset className="flex gap-1">
            <legend className="sr-only">Example call in</legend>
            {(['curl', 'powershell'] as const).map((name) => (
              <button
                key={name}
                type="button"
                aria-pressed={shell === name}
                onClick={() => setShell(name)}
                className="rounded-control px-2 py-0.5 text-xs text-muted hover:text-ink aria-pressed:bg-accent-soft aria-pressed:text-accent"
              >
                {name === 'curl' ? 'curl' : 'PowerShell'}
              </button>
            ))}
          </fieldset>
          <CopyButton text={example} label="Copy the example" />
        </div>
        <pre
          aria-label={`Example call with ${shell === 'curl' ? 'curl' : 'PowerShell'}`}
          className="overflow-x-auto rounded-control border border-line bg-sunken px-3 py-2 font-mono text-xs leading-5"
        >
          {example}
        </pre>
      </div>
    </OnceDialog>
  );
}
