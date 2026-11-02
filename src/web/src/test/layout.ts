import { afterEach, beforeEach, vi } from 'vitest';

/** The log's scrolling region is a window of 480 pixels; everything else, such as a row, is 20 high. */
const height = (element: Element) => (element.getAttribute('role') === 'log' ? 480 : 20);

/**
 * Gives elements a size in the tests of this file. jsdom lays nothing out, so a virtualised list
 * would draw no row and the canvas would compute its view from nothing.
 */
export function withLayout(): void {
  beforeEach(() => {
    vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function size(this: HTMLElement) {
      return new DOMRect(0, 0, 800, height(this));
    });
    vi.spyOn(Element.prototype, 'clientHeight', 'get').mockImplementation(function size(this: Element) {
      return height(this);
    });
    vi.spyOn(Element.prototype, 'clientWidth', 'get').mockReturnValue(800);
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });
}
