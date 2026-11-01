import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import { readSetting, writeSetting } from './storage';
import { ThemeContext, themeKey, type Scheme } from './themeContext';

const darkQuery = '(prefers-color-scheme: dark)';

function storedScheme(): Scheme {
  const value = readSetting(themeKey);
  return value === 'light' || value === 'dark' ? value : 'system';
}

function systemIsDark(): boolean {
  return typeof globalThis.matchMedia === 'function' && globalThis.matchMedia(darkQuery).matches;
}

function resolve(scheme: Scheme): 'light' | 'dark' {
  if (scheme !== 'system') {
    return scheme;
  }
  return systemIsDark() ? 'dark' : 'light';
}

/** Light, dark or the system's choice, remembered per browser and applied to `<html data-theme>`. */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [scheme, setSchemeState] = useState<Scheme>(storedScheme);
  const [resolved, setResolved] = useState(() => resolve(scheme));

  useEffect(() => {
    const apply = () => {
      const next = resolve(scheme);
      setResolved(next);
      document.documentElement.dataset['theme'] = next;
    };
    apply();
    if (scheme !== 'system' || typeof globalThis.matchMedia !== 'function') {
      return undefined;
    }
    const media = globalThis.matchMedia(darkQuery);
    media.addEventListener('change', apply);
    return () => media.removeEventListener('change', apply);
  }, [scheme]);

  const setScheme = useCallback((next: Scheme) => {
    writeSetting(themeKey, next);
    setSchemeState(next);
  }, []);

  const value = useMemo(() => ({ scheme, resolved, setScheme }), [scheme, resolved, setScheme]);
  return <ThemeContext value={value}>{children}</ThemeContext>;
}
