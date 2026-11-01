import { clsx } from 'clsx';
import { useId, type InputHTMLAttributes } from 'react';

export interface TextFieldProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> {
  label: string;
  /** A short help text under the field. */
  hint?: string;
  /** Messages from validation; the first ones the server sent for this field. */
  errors?: readonly string[];
}

const noErrors: readonly string[] = [];

/** A labelled input with its help text and errors wired up for assistive technology. */
export function TextField({ label, hint, errors = noErrors, className, ...rest }: TextFieldProps) {
  const id = useId();
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const invalid = errors.length > 0;
  const describedBy = [hint ? hintId : null, invalid ? errorId : null].filter(Boolean).join(' ');

  return (
    <div className={clsx('flex flex-col gap-1', className)}>
      <label htmlFor={id} className="text-sm font-medium text-ink">
        {label}
      </label>
      <input
        id={id}
        {...rest}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy || undefined}
        className={clsx(
          'h-9 rounded-control border bg-surface px-2.5 text-sm text-ink placeholder:text-muted',
          invalid ? 'border-danger' : 'border-line-strong',
        )}
      />
      {hint && (
        <p id={hintId} className="text-xs text-muted">
          {hint}
        </p>
      )}
      {invalid && (
        <p id={errorId} className="text-xs text-danger">
          {errors.join(' ')}
        </p>
      )}
    </div>
  );
}
