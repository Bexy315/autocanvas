import type { FolderView } from '../api/folders';
import type { PageInfo } from '../layout/pageContext';

/** The breadcrumbs and tree position of a folder's page, or of a page inside the folder (`after`). */
export function folderPageInfo(folder: FolderView, ...after: string[]): PageInfo {
  const above = folder.path.map((crumb) => ({ label: crumb.name, to: crumb.id ? `/folders/${crumb.id}` : null }));
  return {
    crumbs: [
      { label: 'Automations', to: '/' },
      ...above,
      { label: folder.name, to: after.length > 0 ? `/folders/${folder.id}` : null },
      ...after.map((label) => ({ label, to: null })),
    ],
    folderId: folder.id,
    folderPath: folder.path.flatMap((crumb) => (crumb.id ? [crumb.id] : [])),
  };
}
