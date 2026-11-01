import { createContext, useContext, useEffect } from 'react';

/** One step of the breadcrumbs; without `to` it is shown but not a link (no access, or the page itself). */
export interface Crumb {
  label: string;
  to: string | null;
}

/** What a page tells the shell about where it is. */
export interface PageInfo {
  /** The breadcrumbs, from the top to the page itself. */
  crumbs: Crumb[];
  /** The folder the page is about or in, highlighted in the folder tree. */
  folderId?: string;
  /** The folders above it that the user may open, from the top: expanded in the tree. */
  folderPath?: string[];
}

export const PageInfoContext = createContext<{ info: PageInfo | null; setInfo: (info: PageInfo | null) => void }>({
  info: null,
  setInfo: () => {},
});

/** Tells the shell the page's breadcrumbs and folder while the page is shown. */
export function usePageInfo(info: PageInfo | null): void {
  const { setInfo } = useContext(PageInfoContext);
  // Compared by content: pages build the object on every render.
  const key = JSON.stringify(info);
  useEffect(() => {
    setInfo(key === 'null' ? null : (JSON.parse(key) as PageInfo));
    return () => setInfo(null);
  }, [key, setInfo]);
}

export function useCurrentPageInfo(): PageInfo | null {
  return useContext(PageInfoContext).info;
}
