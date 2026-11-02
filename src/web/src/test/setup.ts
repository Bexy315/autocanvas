import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach } from 'vitest';
import { fakeFetch, resetFakeServer } from './fakeServer';

// Installed before any module creates its API client, which keeps a reference to fetch.
globalThis.fetch = fakeFetch;

// jsdom lacks what Radix menus measure and capture with.
class NoResizeObserver {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}
globalThis.ResizeObserver ??= NoResizeObserver;
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};
Element.prototype.scrollIntoView ??= () => {};

// CodeMirror measures text with ranges, which jsdom does not lay out.
const emptyRects = () => Object.assign([], { item: () => null }) as unknown as DOMRectList;
Range.prototype.getClientRects ??= emptyRects;
Range.prototype.getBoundingClientRect ??= () => new DOMRect();
document.elementFromPoint ??= () => null;

afterEach(() => {
  cleanup();
  resetFakeServer();
  globalThis.localStorage.clear();
  delete document.documentElement.dataset['theme'];
});
