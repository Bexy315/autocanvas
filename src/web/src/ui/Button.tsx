import { clsx } from 'clsx';
import type { ButtonHTMLAttributes } from 'react';
import { Spinner } from './Spinner';

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger';

const variants: Record<Variant, string> = {
  primary: 'bg-accent text-accent-ink hover:bg-accent-hover border-transparent',
  secondary: 'bg-surface text-ink border-line-strong hover:bg-sunken',
  ghost: 'bg-transparent text-ink border-transparent hover:bg-sunken',
  danger: 'bg-surface text-danger border-line-strong hover:bg-danger-soft',
};

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant;
  /** Shows a spinner and blocks the button while an action runs. */
  busy?: boolean;
}

export function Button({ variant = 'secondary', busy = false, className, children, disabled, ...rest }: ButtonProps) {
  return (
    <button
      type="button"
      {...rest}
      disabled={disabled === true || busy}
      aria-busy={busy || undefined}
      className={clsx(
        'inline-flex h-8 items-center justify-center gap-1.5 rounded-control border px-3 text-sm font-medium',
        'transition-colors disabled:cursor-not-allowed disabled:opacity-60',
        variants[variant],
        className,
      )}
    >
      {busy && <Spinner size={14} />}
      {children}
    </button>
  );
}
