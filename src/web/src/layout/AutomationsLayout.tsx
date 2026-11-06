import { clsx } from 'clsx';
import { PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import { useState } from 'react';
import { Outlet, useNavigate } from 'react-router';
import { readSetting, writeSetting } from '../app/storage';
import { FolderTree } from './FolderTree';
import { useCurrentPageInfo } from './pageContext';

export const treeKey = 'autocanvas.folder-tree';

/** The Automations area: the folder tree on the left, the folder or automation on the right. */
export function AutomationsLayout() {
  const [collapsed, setCollapsed] = useState(() => readSetting(treeKey) === 'collapsed');
  const info = useCurrentPageInfo();
  const navigate = useNavigate();

  const toggle = () => {
    const next = !collapsed;
    writeSetting(treeKey, next ? 'collapsed' : 'expanded');
    setCollapsed(next);
  };

  return (
    <div className="flex h-full min-h-0">
      <aside
        aria-label="Folders"
        className={clsx('flex shrink-0 flex-col border-r border-line bg-surface', collapsed ? 'w-10' : 'w-60')}
      >
        <div className="flex h-10 items-center justify-between gap-2 border-b border-line px-2">
          {!collapsed && <span className="px-1 text-xs font-medium text-muted">Folders and automations</span>}
          <button
            type="button"
            onClick={toggle}
            aria-expanded={!collapsed}
            aria-label={collapsed ? 'Show folders' : 'Hide folders'}
            title={collapsed ? 'Show folders' : 'Hide folders'}
            className="rounded-control p-1 text-muted hover:bg-sunken hover:text-ink"
          >
            {collapsed ? (
              <PanelLeftOpen aria-hidden="true" size={16} />
            ) : (
              <PanelLeftClose aria-hidden="true" size={16} />
            )}
          </button>
        </div>
        {!collapsed && (
          <div className="min-h-0 flex-1 overflow-auto px-1.5">
            <FolderTree
              label="Folders and automations"
              // On an automation's page the automation is highlighted, inside its folder.
              selectedId={info?.automationId ?? info?.folderId}
              // The page's folder is open too, showing what is in it.
              expandedIds={info?.folderId ? [...(info.folderPath ?? []), info.folderId] : info?.folderPath}
              onChoose={(folder) => void navigate(`/folders/${folder.id}`)}
              onChooseAutomation={(id) => void navigate(`/automations/${id}`)}
            />
          </div>
        )}
      </aside>
      <div className="min-w-0 flex-1 overflow-auto">
        <Outlet />
      </div>
    </div>
  );
}
