import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { SplitView } from '../editor/SplitView';
import { AutomationCanvas } from './AutomationCanvas';
import { CanvasPane, canvasViewKey } from './CanvasPane';
import { toFlow } from './flow';
import { layOut } from './layout';
import { ListView } from './ListView';
import { graphFromYaml, type Graph } from './model';

const source = `version: 1
triggers:
  manual: true
jobs:
  build:
    steps: [{ run: make }, { run: make test }]
  route:
    type: switch
    needs: [build]
    expression: \${{ jobs.build.outputs.ok }}
    cases:
      - { name: ship, when: 'yes' }
      - { name: hold, default: true }
  deploy:
    needs: [{ job: route, case: ship }, { job: build, on: always }]
    steps: [{ type: http_request, with: { url: https://example.com } }]
  merge:
    type: join
    needs: [deploy]
  report:
    finally: true
    steps: [{ run: ./report }]
`;

function graph(text = source): Graph {
  const read = graphFromYaml(text);
  if (!read.graph) {
    throw new Error(read.error);
  }
  return read.graph;
}

describe('the cards for React Flow', () => {
  it('describe each card for assistive technology, problems included', () => {
    const read = graph();
    const { nodes } = toFlow(read, layOut(read), { deploy: 2 });
    const labels = Object.fromEntries(nodes.map((node) => [node.id, node.ariaLabel]));
    expect(labels).toEqual({
      trigger: 'Trigger: By hand',
      build: 'Job build, 2 steps, starts with the run',
      route: 'Switch route, cases ship, hold, needs build',
      deploy: 'Job deploy, 1 step, needs route, build, 2 problems',
      merge: 'Join merge, needs deploy',
      report: 'Finally job report, 1 step, runs last',
    });
  });

  it('come in reading order, top to bottom', () => {
    const read = graph();
    const { nodes } = toFlow(read, layOut(read));
    const ys = nodes.map((node) => node.position.y);
    expect(ys).toEqual(ys.toSorted((a, b) => a - b));
    expect(nodes[0]?.id).toBe('trigger');
    expect(nodes.at(-1)?.id).toBe('report');
  });

  it('draw finally lines dashed and the others by their kind', () => {
    const read = graph();
    const { edges } = toFlow(read, layOut(read));
    expect(edges.filter((edge) => edge.type === 'finally').map((edge) => edge.source)).toEqual(['merge']);
    expect(edges.find((edge) => edge.id === 'build:always->deploy')?.data).toEqual({ kind: 'always', label: 'always' });
    expect(edges.every((edge) => edge.selectable === false && edge.focusable === false)).toBe(true);
  });
});

describe('the canvas', () => {
  it('renders a card for the trigger and every job', () => {
    render(
      <div style={{ width: 800, height: 600 }}>
        <AutomationCanvas graph={graph()} />
      </div>,
    );
    for (const name of ['Trigger: By hand', /^Job build/, /^Switch route/, /^Join merge/, /^Finally job report/]) {
      expect(screen.getByLabelText(name)).toBeInTheDocument();
    }
    expect(screen.getByText('make test')).toBeInTheDocument();
    expect(screen.getByText('= yes')).toBeInTheDocument();
  });

  it('marks the file as unreadable', () => {
    const { container } = render(<AutomationCanvas graph={graph()} stale />);
    expect(container.querySelector('.automation-canvas')).toHaveClass('is-stale');
  });
});

describe('the list view', () => {
  it('lists the jobs in order with what each waits for, and chooses one', async () => {
    const onSelectJob = vi.fn<(id: string) => void>();
    render(<ListView graph={graph()} selectedJob="route" onSelectJob={onSelectJob} />);

    const jobs = screen.getByRole('list', { name: 'Jobs' });
    expect(
      within(jobs)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual([
      'buildjobStarts with the run. 2 steps.',
      'routeswitchNeeds build. Cases: ship (= yes), hold (default).',
      'deployjobNeeds route (ship), build (always). 1 step.',
      'mergejoinNeeds deploy.',
      'reportfinallyRuns last. 1 step.',
    ]);
    expect(within(jobs).getByRole('button', { name: /^route/ })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByText('By hand')).toBeInTheDocument();

    await userEvent.click(within(jobs).getByRole('button', { name: /^deploy/ }));
    expect(onSelectJob).toHaveBeenCalledWith('deploy');
  });

  it('shows problems and missing triggers', () => {
    render(<ListView graph={graph('version: 1\njobs:\n  a:\n    needs: [b]\n    steps: [{ run: x }]\n')} />);
    expect(screen.getByText('Nothing starts it yet.')).toBeInTheDocument();
    expect(screen.getByText('Needs b, which is not a job of this file.')).toBeInTheDocument();
  });
});

describe('the canvas pane', () => {
  it('switches between graph and list and remembers it', async () => {
    const user = userEvent.setup();
    const props = {
      graph: graph(),
      stale: false,
      selectedJob: null,
      problemsByJob: {},
      onSelectJob: vi.fn<(id: string) => void>(),
    };
    const { unmount } = render(<CanvasPane {...props} />);

    await user.click(screen.getByRole('button', { name: 'List' }));
    expect(screen.getByRole('list', { name: 'Jobs' })).toBeInTheDocument();
    expect(localStorage.getItem(canvasViewKey)).toBe('list');
    unmount();

    render(<CanvasPane {...props} stale />);
    expect(screen.getByRole('list', { name: 'Jobs' })).toBeInTheDocument();
    expect(screen.getByText(/showing the last readable state/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Graph' }));
    expect(screen.queryByRole('list', { name: 'Jobs' })).not.toBeInTheDocument();
  });
});

/** A split view in `mode`, reporting resizes to `onRatio`. */
function split(mode: 'canvas' | 'split' | 'yaml', onRatio = vi.fn<(ratio: number) => void>()) {
  render(
    <SplitView
      mode={mode}
      ratio={0.5}
      onRatio={onRatio}
      left="left side"
      right="right side"
      leftLabel="Canvas"
      rightLabel="File"
    />,
  );
  return onRatio;
}

describe('the split view', () => {
  it('is resized with the arrow keys, within limits', () => {
    const onRatio = split('split');
    const separator = screen.getByRole('separator', { name: 'Resize canvas and file' });
    expect(separator).toHaveAttribute('aria-valuenow', '50');

    fireEvent.keyDown(separator, { key: 'ArrowLeft' });
    fireEvent.keyDown(separator, { key: 'ArrowRight' });
    fireEvent.keyDown(separator, { key: 'Home' });
    fireEvent.keyDown(separator, { key: 'End' });
    fireEvent.keyDown(separator, { key: 'a' });

    expect(onRatio.mock.calls.map(([value]) => Math.round(Number(value) * 100))).toEqual([45, 55, 20, 80]);
  });

  it('is dragged with the pointer', () => {
    const onRatio = split('split');
    const separator = screen.getByRole('separator');
    const host = separator.parentElement;
    if (!host) {
      throw new Error('No host.');
    }
    host.getBoundingClientRect = () => ({ left: 0, width: 1000 }) as DOMRect;
    separator.setPointerCapture = () => {};
    separator.releasePointerCapture = () => {};

    fireEvent.pointerDown(separator, { pointerId: 1 });
    fireEvent.pointerMove(separator, { clientX: 300 });
    fireEvent.pointerUp(separator, { pointerId: 1 });
    fireEvent.pointerMove(separator, { clientX: 900 });

    expect(onRatio.mock.calls).toEqual([[0.3]]);
  });

  it('shows one side only, keeping the other mounted', () => {
    split('canvas');
    expect(screen.queryByRole('separator')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Canvas' })).toBeVisible();
    expect(screen.getByText('right side')).toBeInTheDocument();
    expect(screen.getByText('right side').closest('section')).toHaveClass('hidden');
  });
});
