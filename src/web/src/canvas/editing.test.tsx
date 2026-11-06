import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { DragEvent } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { smallestChange } from '../editor/smallestChange';
import { AutomationCanvas, type CanvasEdits } from './AutomationCanvas';
import { CanvasPane } from './CanvasPane';
import { toFlow } from './flow';
import { freeSpot, layOut } from './layout';
import { graphFromYaml, type Graph } from './model';
import { pinUnplaced, type NodeTypeView } from './operations';
import { Palette } from './Palette';
import { kindOfDrop, paletteMime } from './paletteEntries';

const httpRequest: NodeTypeView = {
  name: 'http_request',
  description: 'Sends an HTTP request.',
  inputs: [],
  outputs: [],
};

function graph(text: string): Graph {
  const read = graphFromYaml(text);
  if (!read.graph) {
    throw new Error(read.error);
  }
  return read.graph;
}

function edits(): CanvasEdits {
  return {
    move: vi.fn<CanvasEdits['move']>(),
    add: vi.fn<CanvasEdits['add']>(),
    connect: vi.fn<CanvasEdits['connect']>(),
    canConnect: vi.fn<CanvasEdits['canConnect']>(() => true),
    disconnect: vi.fn<CanvasEdits['disconnect']>(),
    remove: vi.fn<CanvasEdits['remove']>(),
    undo: vi.fn<CanvasEdits['undo']>(),
    redo: vi.fn<CanvasEdits['redo']>(),
  };
}

describe('the smallest change', () => {
  it.each([
    ['abc', 'abc', { from: 3, to: 3, insert: '' }],
    ['abc', 'abXc', { from: 2, to: 2, insert: 'X' }],
    ['abc', 'ac', { from: 1, to: 2, insert: '' }],
    ['aaa', 'aaaa', { from: 3, to: 3, insert: 'a' }],
    ['', 'new', { from: 0, to: 0, insert: 'new' }],
  ])('from %j to %j', (before, after, change) => {
    expect(smallestChange(before, after)).toEqual(change);
    expect(before.slice(0, change.from) + change.insert + before.slice(change.to)).toBe(after);
  });
});

describe('placing cards', () => {
  it('pins every card shown before the first change, once', () => {
    const text = 'version: 1\njobs:\n  a:\n    steps: [{ run: x }]\n';
    const pinned = pinUnplaced(text);
    expect(Object.keys(graph(pinned).positions).toSorted()).toEqual(['a', 'trigger']);
    expect(pinUnplaced(pinned)).toBe(pinned);
    expect(pinUnplaced('jobs: [')).toBe('jobs: [');
  });

  it('finds a free place below cards that are in the way', () => {
    expect(freeSpot({ x: 0, y: 0 }, {})).toEqual({ x: 0, y: 0 });
    expect(freeSpot({ x: 0, y: 0 }, { a: { x: 10, y: 0 }, b: { x: 10, y: 140 } })).toEqual({ x: 0, y: 280 });
    expect(freeSpot({ x: 0, y: 0 }, { far: { x: 600, y: 0 } })).toEqual({ x: 0, y: 0 });
  });
});

/** A drop event carrying `key` as the palette's data. */
const drop = (key: string) => ({ dataTransfer: { getData: () => key } }) as unknown as DragEvent;

describe('the palette', () => {
  it('adds what is chosen, also by keyboard', async () => {
    const onAdd = vi.fn<(kind: unknown) => void>();
    render(<Palette nodeTypes={[httpRequest]} onAdd={onAdd} />);
    const bar = screen.getByRole('region', { name: 'Add to the canvas' });

    await userEvent.click(within(bar).getByRole('button', { name: /^Add Switch:/ }));
    within(bar)
      .getByRole('button', { name: /^Add http_request:/ })
      .focus();
    await userEvent.keyboard('{Enter}');

    expect(onAdd.mock.calls).toEqual([['switch'], [{ nodeType: httpRequest }]]);
  });

  it('groups flow and node types, and narrows them with a filter', async () => {
    render(<Palette nodeTypes={[httpRequest]} onAdd={vi.fn<(kind: unknown) => void>()} />);
    const panel = screen.getByRole('region', { name: 'Add to the canvas' });
    expect(within(panel).getByRole('heading', { name: 'Flow' })).toBeInTheDocument();
    expect(within(panel).getByRole('heading', { name: 'Node types' })).toBeInTheDocument();

    await userEvent.type(within(panel).getByRole('searchbox', { name: 'Filter what can be added' }), 'http');
    expect(within(panel).queryByRole('heading', { name: 'Flow' })).not.toBeInTheDocument();
    expect(within(panel).getAllByRole('button', { name: /^Add / })).toHaveLength(1);

    await userEvent.clear(within(panel).getByRole('searchbox', { name: 'Filter what can be added' }));
    await userEvent.type(within(panel).getByRole('searchbox', { name: 'Filter what can be added' }), 'waits');
    expect(within(panel).getByRole('button', { name: /^Add Join:/ })).toBeInTheDocument();

    await userEvent.type(within(panel).getByRole('searchbox', { name: 'Filter what can be added' }), 'zzz');
    expect(within(panel).getByText('Nothing matches “waitszzz”.')).toBeInTheDocument();
  });

  it('is a bar of icons next to the open settings, with names for screen readers and tooltips', () => {
    render(<Palette nodeTypes={[httpRequest]} onAdd={vi.fn<(kind: unknown) => void>()} compact />);
    const panel = screen.getByRole('region', { name: 'Add to the canvas' });
    expect(panel).toHaveClass('w-11');
    expect(panel).not.toHaveClass('@2xl/canvas:w-56');
    expect(screen.getByRole('searchbox', { name: 'Filter what can be added', hidden: true }).parentElement).toHaveClass(
      'hidden',
    );
    const job = within(panel).getByRole('button', { name: /^Add Job:/ });
    expect(job).toHaveAttribute('title', expect.stringMatching(/^Job: Runs steps on a runner\./));
  });

  it('hides and shows again, and the browser remembers it', async () => {
    const { unmount } = render(<Palette nodeTypes={[]} onAdd={vi.fn<(kind: unknown) => void>()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Hide what can be added' }));
    expect(screen.queryByRole('region', { name: 'Add to the canvas' })).not.toBeInTheDocument();
    unmount();

    render(<Palette nodeTypes={[]} onAdd={vi.fn<(kind: unknown) => void>()} />);
    expect(screen.queryByRole('region', { name: 'Add to the canvas' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Show what can be added' }));
    expect(screen.getByRole('region', { name: 'Add to the canvas' })).toBeInTheDocument();
    expect(localStorage.getItem('autocanvas.canvas.palette')).toBe('open');
  });

  it('carries the kind when dragged, and reads it back on the drop', () => {
    render(<Palette nodeTypes={[httpRequest]} onAdd={vi.fn<(kind: unknown) => void>()} />);
    const data = new Map<string, string>();
    const dataTransfer = { setData: (type: string, value: string) => data.set(type, value), effectAllowed: '' };
    fireEvent.dragStart(screen.getByRole('button', { name: /^Add Join:/ }), { dataTransfer });
    expect(data.get(paletteMime)).toBe('join');

    expect(kindOfDrop(drop('finally'), [])).toBe('finally');
    expect(kindOfDrop(drop('node:http_request'), [httpRequest])).toEqual({ nodeType: httpRequest });
    expect(kindOfDrop(drop('text/plain'), [httpRequest])).toBeNull();
  });
});

describe('the canvas for editors', () => {
  const read = graph(
    'version: 1\njobs:\n  a:\n    steps: [{ run: x }]\n  b:\n    needs: [a]\n    steps: [{ run: y }]\n',
  );

  it('offers the palette, undo and redo by keyboard, and makes lines selectable', () => {
    const actions = edits();
    const { container } = render(
      <div style={{ width: 800, height: 600 }}>
        <AutomationCanvas graph={read} edits={actions} paletteNodeTypes={[httpRequest]} />
      </div>,
    );
    expect(screen.getByRole('region', { name: 'Add to the canvas' })).toBeInTheDocument();
    const surface = container.querySelector('.automation-canvas');
    if (!surface) {
      throw new Error('No canvas.');
    }

    fireEvent.keyDown(surface, { key: 'z', ctrlKey: true });
    fireEvent.keyDown(surface, { key: 'z', ctrlKey: true, shiftKey: true });
    fireEvent.keyDown(surface, { key: 'y', metaKey: true });
    fireEvent.keyDown(surface, { key: 'z' });

    expect(actions.undo).toHaveBeenCalledTimes(1);
    expect(actions.redo).toHaveBeenCalledTimes(2);
  });

  it('adds what the palette chooses in the middle of the view', async () => {
    const actions = edits();
    render(<AutomationCanvas graph={read} edits={actions} />);

    await userEvent.click(screen.getByRole('button', { name: /^Add Job:/ }));

    expect(actions.add).toHaveBeenCalledWith('job', expect.objectContaining({ x: expect.any(Number) }));
  });

  it('cannot be changed while the file cannot be read, nor by viewers', () => {
    const actions = edits();
    const { rerender, container } = render(<AutomationCanvas graph={read} edits={actions} stale />);
    expect(screen.queryByRole('region', { name: 'Add to the canvas' })).not.toBeInTheDocument();
    fireEvent.keyDown(container.querySelector('.automation-canvas') ?? document.body, { key: 'z', ctrlKey: true });
    expect(actions.undo).not.toHaveBeenCalled();

    rerender(<AutomationCanvas graph={read} />);
    expect(screen.queryByRole('region', { name: 'Add to the canvas' })).not.toBeInTheDocument();
  });

  it('lets editors choose and delete the lines from needs, not the drawn ones', () => {
    const editable = toFlow(read, layOut(read), {}, true);
    const flags = Object.fromEntries(editable.edges.map((edge) => [edge.id, [edge.selectable, edge.deletable]]));
    expect(flags).toEqual({ 'start->a': [false, false], 'a:success->b': [true, true] });
    expect(editable.nodes.find((node) => node.id === 'a')?.deletable).toBe(true);
    expect(toFlow(read, layOut(read)).edges.every((edge) => edge.selectable === false)).toBe(true);
  });
});

describe('the canvas pane for editors', () => {
  it('has undo, redo and tidy up, and announces changes', async () => {
    const actions = edits();
    const onTidy = vi.fn<() => void>();
    render(
      <CanvasPane
        graph={graph('version: 1\njobs:\n  a:\n    steps: [{ run: x }]\n')}
        stale={false}
        selectedJob={null}
        problemsByJob={{}}
        onSelectJob={vi.fn<(id: string) => void>()}
        edits={actions}
        onTidy={onTidy}
        announcement="Added job job2"
      />,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Undo (Ctrl+Z)' }));
    await userEvent.click(screen.getByRole('button', { name: 'Redo (Ctrl+Y)' }));
    await userEvent.click(screen.getByRole('button', { name: 'Tidy up' }));

    expect(actions.undo).toHaveBeenCalled();
    expect(actions.redo).toHaveBeenCalled();
    expect(onTidy).toHaveBeenCalled();
    expect(screen.getByText('Added job job2')).toBeInTheDocument();
  });
});
