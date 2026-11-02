import { Copy, TriangleAlert } from 'lucide-react';
import { useId, useState } from 'react';
import type { CreatedWebhook } from '../api/configuration';
import { Button } from '../ui/Button';
import { Dialog, DialogActions } from '../ui/Dialog';
import { curlExample, powerShellExample } from './webhookExamples';

function CopyButton({ text, label }: { text: string; label: string }) {
  const [copied, setCopied] = useState(false);
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={() =>
        void navigator.clipboard?.writeText(text).then(
          () => setCopied(true),
          () => setCopied(false),
        )
      }
      className="flex shrink-0 items-center gap-1 rounded-control px-2 py-1 text-xs text-muted hover:bg-sunken hover:text-ink"
    >
      <Copy aria-hidden="true" size={12} />
      {copied ? 'Copied' : 'Copy'}
    </button>
  );
}

function Value({ label, value }: { label: string; value: string }) {
  const id = useId();
  return (
    <div className="flex flex-col gap-1">
      <span id={id} className="text-sm font-medium">
        {label}
      </span>
      <div className="flex items-center gap-2 rounded-control border border-line-strong bg-sunken px-2 py-1.5">
        <code aria-labelledby={id} className="min-w-0 flex-1 font-mono text-xs break-all">
          {value}
        </code>
        <CopyButton text={value} label={`Copy the ${label.toLowerCase()}`} />
      </div>
    </div>
  );
}

/**
 * The new webhook address and signing secret, shown this one time, with example calls. It closes
 * only once the user says they saved them, so Esc or a stray click cannot lose them.
 */
export function WebhookCreatedDialog({ created, onClose }: { created: CreatedWebhook; onClose: () => void }) {
  const [saved, setSaved] = useState(false);
  const [shell, setShell] = useState<'curl' | 'powershell'>('curl');
  const url = `${globalThis.location.origin}${created.path}`;
  const secret = created.signingSecret ?? null;
  const example = shell === 'curl' ? curlExample(url, secret) : powerShellExample(url, secret);

  return (
    <Dialog
      open
      wide
      onOpenChange={(open) => !open && saved && onClose()}
      title="Webhook address created"
      description="Calls of this address start a run; with a signature only when they are signed."
    >
      <div className="flex flex-col gap-4">
        <p className="flex gap-2 rounded-control bg-warn-soft px-3 py-2 text-sm">
          <TriangleAlert aria-hidden="true" size={16} className="mt-0.5 shrink-0 text-warn" />
          They are shown only now. Copy them to where the caller keeps its secrets; to replace them, create the address
          again.
        </p>
        {!created.enabled && (
          <p className="text-sm text-muted">
            Calls start runs once the automation's file has <code className="font-mono">webhook: true</code> under
            triggers.
          </p>
        )}
        <Value label="Address" value={url} />
        {secret && <Value label="Signing secret" value={secret} />}
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
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={saved}
            onChange={(event) => setSaved(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          I have saved the address{secret ? ' and the secret' : ''}
        </label>
        <DialogActions>
          <Button variant="primary" disabled={!saved} onClick={onClose}>
            Done
          </Button>
        </DialogActions>
      </div>
    </Dialog>
  );
}
