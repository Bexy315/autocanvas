import type { ReactNode } from 'react';
import { useServerInfo } from '../api/session';
import { shortVersion } from '../app/version';
import { LogoMark } from '../ui/Logo';

/** Pages before the app: a single card on the canvas's dotted background. */
export function AuthLayout({ title, intro, children }: { title: string; intro?: ReactNode; children: ReactNode }) {
  const info = useServerInfo();
  return (
    <div className="canvas-grid flex min-h-full flex-col items-center justify-center gap-6 px-4 py-10">
      <main className="w-full max-w-sm rounded-panel border border-line bg-surface p-6 shadow-sm">
        <div className="mb-5 flex items-center gap-2">
          <LogoMark size={28} />
          <span className="text-base font-semibold tracking-tight">AutoCanvas</span>
        </div>
        <h1 className="text-lg font-semibold">{title}</h1>
        {intro && <div className="mt-1 text-muted">{intro}</div>}
        <div className="mt-5">{children}</div>
      </main>
      {info.data && (
        <p className="text-xs text-muted" title={info.data.version}>
          Version {shortVersion(info.data.version)}
        </p>
      )}
    </div>
  );
}
