import { act, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ThemeProvider } from './theme';
import { themeKey, useTheme } from './themeContext';

function Probe() {
  const { scheme, resolved, setScheme } = useTheme();
  return (
    <div>
      <p>
        {scheme}/{resolved}
      </p>
      <button type="button" onClick={() => setScheme('dark')}>
        dark
      </button>
      <button type="button" onClick={() => setScheme('system')}>
        system
      </button>
    </div>
  );
}

/** A system color scheme that the test can switch, like the OS setting. */
function systemScheme(dark: boolean) {
  const listeners = new Set<() => void>();
  const media = {
    get matches() {
      return dark;
    },
    addEventListener: (_: string, listener: () => void) => listeners.add(listener),
    removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
  };
  vi.stubGlobal('matchMedia', () => media);
  return {
    switchTo(next: boolean) {
      dark = next;
      for (const listener of listeners) {
        listener();
      }
    },
  };
}

describe('the color scheme', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('follows the system until the user picks one, and remembers the pick', () => {
    const system = systemScheme(false);
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    expect(screen.getByText('system/light')).toBeInTheDocument();
    expect(document.documentElement.dataset['theme']).toBe('light');

    act(() => system.switchTo(true));
    expect(screen.getByText('system/dark')).toBeInTheDocument();
    expect(document.documentElement.dataset['theme']).toBe('dark');

    act(() => screen.getByRole('button', { name: 'dark' }).click());
    act(() => system.switchTo(false));
    expect(screen.getByText('dark/dark')).toBeInTheDocument();
    expect(localStorage.getItem(themeKey)).toBe('dark');
  });

  it('starts with the stored pick', () => {
    systemScheme(true);
    localStorage.setItem(themeKey, 'light');
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    expect(screen.getByText('light/light')).toBeInTheDocument();
  });

  it('is light when the browser cannot say', () => {
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    );
    expect(screen.getByText('system/light')).toBeInTheDocument();
  });

  it('needs a provider', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Probe />)).toThrow('useTheme needs a ThemeProvider.');
  });
});
