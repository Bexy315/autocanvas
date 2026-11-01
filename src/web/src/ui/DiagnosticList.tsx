import { clsx } from 'clsx';
import { CircleAlert, TriangleAlert } from 'lucide-react';
import type { Diagnostic } from '../api/problem';

function place(diagnostic: Diagnostic): string {
  if (diagnostic.line === null || diagnostic.line === undefined) {
    return diagnostic.path || 'File';
  }
  const column = diagnostic.column === null || diagnostic.column === undefined ? '' : `:${diagnostic.column}`;
  return `Line ${diagnostic.line}${column}`;
}

/** Problems and warnings in an automation file, with where they are and their code. */
export function DiagnosticList({ diagnostics, label }: { diagnostics: readonly Diagnostic[]; label: string }) {
  return (
    <ul aria-label={label} className="flex flex-col divide-y divide-line rounded-control border border-line">
      {diagnostics.map((diagnostic) => {
        const error = diagnostic.severity === 'Error';
        const Icon = error ? CircleAlert : TriangleAlert;
        return (
          <li
            key={`${diagnostic.code}-${diagnostic.path}-${diagnostic.line ?? ''}-${diagnostic.column ?? ''}-${diagnostic.message}`}
            className="flex gap-2 px-3 py-2"
          >
            <Icon
              aria-label={error ? 'Error' : 'Warning'}
              size={16}
              className={clsx('mt-0.5 shrink-0', error ? 'text-danger' : 'text-warn')}
            />
            <div className="flex min-w-0 flex-col gap-0.5">
              <p>{diagnostic.message}</p>
              <p className="text-xs text-muted">
                {place(diagnostic)} · <code className="font-mono">{diagnostic.code}</code>
                {diagnostic.path && diagnostic.line !== null && diagnostic.line !== undefined && (
                  <>
                    {' '}
                    · <code className="font-mono">{diagnostic.path}</code>
                  </>
                )}
              </p>
            </div>
          </li>
        );
      })}
    </ul>
  );
}
