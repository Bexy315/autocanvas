import { Folder, Lock, Workflow } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router';
import type { AutomationSummary, FolderSummary } from '../api/folders';
import { Badge } from '../ui/Badge';
import { RelativeTime } from '../ui/RelativeTime';

/** One entry in a list of folders and automations. */
function Row({
  to,
  icon,
  title,
  meta,
  children,
}: {
  to: string;
  icon: ReactNode;
  title: ReactNode;
  meta?: ReactNode;
  children?: ReactNode;
}) {
  return (
    <li>
      <Link to={to} className="flex items-start gap-3 px-4 py-2.5 hover:bg-sunken">
        <span className="mt-0.5 shrink-0 text-muted">{icon}</span>
        <span className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="flex flex-wrap items-center gap-2">
            <span className="truncate font-medium text-ink">{title}</span>
          </span>
          {children && <span className="truncate text-muted">{children}</span>}
        </span>
        {meta && <span className="flex shrink-0 items-center gap-3 text-xs text-muted">{meta}</span>}
      </Link>
    </li>
  );
}

export function FolderRow({
  folder,
  path,
}: {
  folder: Pick<FolderSummary, 'id' | 'name' | 'description' | 'isRestricted' | 'myRole'>;
  path?: string;
}) {
  return (
    <Row
      to={`/folders/${folder.id}`}
      icon={<Folder aria-hidden="true" size={16} />}
      title={
        <>
          {folder.name}
          {folder.isRestricted && <Lock aria-label="Restricted" size={12} className="ml-1.5 inline text-muted" />}
        </>
      }
      meta={<Badge>{folder.myRole}</Badge>}
    >
      {path ? `In ${path}` : folder.description}
    </Row>
  );
}

export function AutomationRow({ automation }: { automation: AutomationSummary }) {
  return (
    <Row
      to={`/automations/${automation.id}`}
      icon={<Workflow aria-hidden="true" size={16} />}
      title={automation.title}
      meta={
        <>
          <span>Version {automation.currentVersion}</span>
          <span>
            Updated <RelativeTime value={automation.updatedAt} />
          </span>
        </>
      }
    >
      {automation.title === automation.name ? (
        automation.description
      ) : (
        <>
          <code className="font-mono text-xs">{automation.name}</code>
          {automation.description && <> — {automation.description}</>}
        </>
      )}
    </Row>
  );
}
