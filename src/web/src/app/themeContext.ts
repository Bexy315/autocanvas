import { createContext, useContext } from 'react';

export type Scheme = 'system' | 'light' | 'dark';

/** Must match public/theme.js, which applies the scheme before the app loads. */
export const themeKey = 'autocanvas.theme';

export interface ThemeValue {
  /** What the user picked. */
  scheme: Scheme;
  /** What is shown, with `system` resolved. */
  resolved: 'light' | 'dark';
  setScheme: (scheme: Scheme) => void;
}

export const ThemeContext = createContext<ThemeValue | null>(null);

export function useTheme(): ThemeValue {
  const value = useContext(ThemeContext);
  if (!value) {
    throw new Error('useTheme needs a ThemeProvider.');
  }
  return value;
}
