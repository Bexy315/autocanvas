import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import type { Diagnostic } from '../../api/problem';
import { graphFromYaml } from '../model';
import type { Change, NodeTypeView } from '../operations';
import { isPlainMap, isTextList, pathText, problemsAt, problemsUnder, readPanelFile } from '../panelModel';
import type { PanelEdits } from './panelContext';
import { SidePanel, type PanelSubject } from './SidePanel';

const httpRequest: NodeTypeView = {
  name: 'http_request',
  description: 'Sends an HTTP request.',
  inputs: [
    { name: 'url', kind: 'Text', required: true, description: 'The address.', choices: [] },
    { name: 'method', kind: 'Text', required: false, description: 'The method.', choices: ['GET', 'POST'] },
  ],
  outputs: [{ name: 'status', description: 'The status code.' }],
};

const everyKind: NodeTypeView = {
  name: 'kinds',
  description: 'Takes one input of each kind.',
  inputs: [
    { name: 'count', kind: 'WholeNumber', required: false, description: 'A number.', choices: [] },
    { name: 'flag', kind: 'Boolean', required: false, description: 'Yes or no.', choices: [] },
    { name: 'tags', kind: 'TextList', required: false, description: 'Some texts.', choices: [] },
    { name: 'headers', kind: 'TextMap', required: false, description: 'Names and values.', choices: [] },
  ],
  outputs: [],
};

const file = `# Site
version: 1
triggers:
  manual: true
jobs:
  build:   # compiles
    runs_on: linux
    image: alpine:3.22
    steps:
    - run: make
  deploy:
    needs: [build]
    steps:
    - run: ./deploy \${{ jobs.build.outputs.v }}
  route:
    type: switch
    expression: \${{ trigger.event }}
    cases:
      - { name: manual, when: manual }
      - { name: other, default: true }
`;

const noDiagnostics: Diagnostic[] = [];

/** The side panel on a file that it changes, as the editor would. */
function Harness({
  initial = file,
  subject,
  readOnly = false,
  diagnostics = noDiagnostics,
  onText,
}: {
  initial?: string;
  subject: PanelSubject;
  readOnly?: boolean;
  diagnostics?: Diagnostic[];
  onText?: (text: string) => void;
}) {
  const [text, setText] = useState(initial);
  const current = useRef(initial);
  const [shown, setShown] = useState(subject);
  const edits: PanelEdits = {
    current: () => current.current,
    apply: (change: Change | null) => {
      if (change) {
        current.current = change.text;
        setText(change.text);
        onText?.(change.text);
        if (change.focus) {
          setShown({ id: change.focus });
        }
      }
    },
    remove: vi.fn<(id: string) => void>(),
  };
  const graph = graphFromYaml(text).graph ?? { triggers: [], jobs: [], edges: [], positions: {} };
  return (
    <>
      <SidePanel
        subject={shown}
        text={text}
        graph={graph}
        nodeTypes={[httpRequest, everyKind]}
        diagnostics={diagnostics}
        {...(readOnly ? {} : { edits })}
        onClose={vi.fn<() => void>()}
      />
      <pre data-testid="text">{text}</pre>
    </>
  );
}

const text = () => screen.getByTestId('text').textContent ?? '';

describe('the panel model', () => {
  it('reads values, knows shared ones, and finds problems by path', () => {
    const read = readPanelFile('a: &x {b: 1}\nc: *x\nd: [1, 2]\n');
    expect(read?.get(['d'])).toEqual([1, 2]);
    expect(read?.get(['nope'])).toBeUndefined();
    expect(read?.shared(['a'])).toBe(true);
    expect(read?.shared(['d'])).toBe(false);
    expect(read?.source(['d'])).toBe('[1, 2]');
    expect(readPanelFile('a: [')).toBeNull();
    expect(isPlainMap({ a: 1, b: 'x' })).toBe(true);
    expect(isPlainMap({ a: [] })).toBe(false);
    expect(isTextList(['a'])).toBe(true);
    expect(pathText(['jobs', 'build', 'steps', 0, 'run'])).toBe('jobs.build.steps[0].run');
    const diagnostics: Diagnostic[] = [
      { severity: 'Error', code: 'X', message: 'Bad run.', path: 'jobs.build.steps[0].run' },
      { severity: 'Error', code: 'X', message: 'Bad job.', path: 'jobs.build' },
    ];
    expect(problemsAt(diagnostics, ['jobs', 'build', 'steps', 0, 'run'])).toEqual(['Bad run.']);
    expect(problemsUnder(diagnostics, ['jobs', 'build'])).toBe(2);
    expect(problemsUnder(diagnostics, ['jobs', 'buildx'])).toBe(0);
  });
});

describe('the job panel', () => {
  it('changes job settings as they are typed, keeping the rest of the file', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'build' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of build' });

    const image = within(panel).getByLabelText('Image');
    await user.clear(image);
    await user.type(image, 'node:24');
    expect(text()).toContain('  build:   # compiles\n    runs_on: linux\n    image: node:24\n');

    await user.type(within(panel).getByLabelText('Runner labels'), 'docker{Enter}');
    expect(text()).toContain('runs_on: [linux, docker]');
    await user.click(within(panel).getByRole('button', { name: 'Remove docker' }));
    expect(text()).toContain('runs_on: linux\n');

    await user.click(within(panel).getByRole('button', { name: /^Limits/ }));
    await user.type(within(panel).getByLabelText('Attempts in total'), '3');
    expect(text()).toContain('    retry:\n      max_attempts: 3\n');
    await user.clear(within(panel).getByLabelText('Attempts in total'));
    expect(text()).not.toContain('retry');
  });

  it('adds, edits, moves and deletes steps', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'build' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of build' });

    await user.click(within(panel).getByRole('button', { name: 'Add step' }));
    await user.click(within(panel).getByRole('button', { name: 'Script' }));
    const script = within(panel).getByLabelText('Script');
    await user.clear(script);
    await user.type(script, 'npm ci{Enter}npm test');
    expect(text()).toContain('    - run: |-\n        npm ci\n        npm test\n');

    await user.click(within(panel).getByRole('button', { name: 'Move step 2 up' }));
    expect(text()).toMatch(/steps:\n {4}- run: \|-\n {8}npm ci\n {8}npm test\n {4}- run: make\n/);
    await user.click(within(panel).getByRole('button', { name: 'Delete step 1' }));
    expect(text()).not.toContain('npm ci');

    await user.click(within(panel).getByRole('button', { name: 'Add step' }));
    await user.click(within(panel).getByRole('button', { name: 'http_request' }));
    await user.type(within(panel).getByLabelText('url (required)'), 'https://example.com');
    await user.selectOptions(within(panel).getByLabelText('method'), 'POST');
    expect(text()).toContain(
      '    - type: http_request\n      with:\n        url: https://example.com\n        method: POST\n',
    );
    expect(within(panel).getByText('status')).toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: /^More/ }));
    await user.type(within(panel).getByLabelText('Id'), 'hook');
    await user.click(within(panel).getByLabelText('Continue when this step fails'));
    expect(text()).toContain('        method: POST\n      id: hook\n      continue_on_error: true\n');
  });

  it('shows inputs of every kind, and takes an expression instead', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'build' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of build' });
    await user.click(within(panel).getByRole('button', { name: 'Add step' }));
    await user.click(within(panel).getByRole('button', { name: 'kinds' }));

    await user.type(within(panel).getByLabelText('count'), '4');
    await user.selectOptions(within(panel).getByLabelText('flag'), 'true');
    await user.type(within(panel).getByLabelText('tags'), 'a{Enter}');
    await user.click(within(panel).getByRole('button', { name: 'Add name' }));
    await user.type(within(panel).getByLabelText('New name'), 'Accept{Enter}');
    await user.type(within(panel).getByLabelText('Value of Accept'), 'json');
    expect(text()).toContain(
      '      with:\n        count: 4\n        flag: true\n        tags: [a]\n        headers:\n          Accept: json\n',
    );

    await user.click(within(panel).getByRole('button', { name: 'Use an expression for count' }));
    const count = within(panel).getByLabelText('count');
    fireEvent.change(count, { target: { value: '${{ vars.N }}' } });
    expect(text()).toContain('count: ${{ vars.N }}');
  });

  it('renames the job, with what names it, and refuses ids that cannot be', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'build' }} />);
    const id = within(screen.getByRole('complementary')).getByLabelText('Id');
    await user.clear(id);
    await user.type(id, 'deploy');
    expect(screen.getByText('There is a job deploy already.')).toBeInTheDocument();
    await user.clear(id);
    await user.type(id, 'compile{Enter}');
    expect(text()).toContain('  compile:   # compiles\n');
    expect(text()).toContain('needs: [compile]');
    expect(text()).toContain('${{ jobs.compile.outputs.v }}');
    expect(screen.getByRole('complementary', { name: 'Settings of compile' })).toBeInTheDocument();
  });

  it('sets what a job waits for with the keyboard', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'deploy' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of deploy' });
    await user.selectOptions(within(panel).getByLabelText('Starts'), 'always');
    expect(text()).toContain('needs: [{job: build, on: always}]');
    await user.click(within(panel).getByRole('button', { name: 'Add a job to wait for' }));
    expect(text()).toContain('needs: [{job: build, on: always}, {job: route, case: manual}]');
    const second = within(panel).getAllByLabelText('Starts')[1];
    if (!second) {
      throw new Error('No second need.');
    }
    await user.selectOptions(second, 'case:other');
    expect(text()).toContain('{job: route, case: other}');
    await user.click(within(panel).getByRole('button', { name: 'Stop waiting for build' }));
    await user.click(within(panel).getByRole('button', { name: 'Stop waiting for route' }));
    expect(text()).not.toMatch(/ {4}needs:/);
  });

  it('adds services, checkouts and reports', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'build' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of build' });

    await user.click(within(panel).getByRole('button', { name: /^Services/ }));
    await user.click(within(panel).getByRole('button', { name: 'Add a service' }));
    await user.type(within(panel).getByLabelText('Name of the new service'), 'db{Enter}');
    expect(text()).toContain('    services:\n      db:\n        preset: postgres\n');
    await user.type(within(panel).getByLabelText('Version'), '17');
    await user.selectOptions(within(panel).getByLabelText('From'), 'image');
    expect(text()).toContain('      db:\n        image: postgres:17\n');
    await user.click(within(panel).getByRole('button', { name: 'Remove the service db' }));
    expect(text()).not.toContain('services');

    await user.click(within(panel).getByRole('button', { name: /^Checkout/ }));
    await user.selectOptions(within(panel).getByLabelText('Before the steps'), 'none');
    expect(text()).toContain('    checkout: false\n');
    await user.selectOptions(within(panel).getByLabelText('Before the steps'), 'list');
    await user.type(within(panel).getByLabelText('Ref'), 'main');
    expect(text()).toContain('    checkout:\n      - repository: self\n        ref: main\n');

    await user.click(within(panel).getByRole('button', { name: /^Reports/ }));
    await user.click(within(panel).getByRole('button', { name: 'Add a report' }));
    await user.selectOptions(within(panel).getByLabelText('Format'), 'trx');
    expect(text()).toContain('    reports:\n      - format: trx\n        path: "**/TEST-*.xml"\n');
  });

  it('edits a switch and its cases', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'route' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of route' });
    expect(within(panel).queryByRole('button', { name: 'Add step' })).not.toBeInTheDocument();

    await user.click(within(panel).getByRole('button', { name: 'Add a case' }));
    expect(text()).toContain(
      '      - { name: manual, when: manual }\n      - {name: case3, when: ""}\n      - { name: other, default: true }\n',
    );
    const value = within(panel).getAllByLabelText('Value')[1];
    if (!value) {
      throw new Error('No value field.');
    }
    await user.type(value, 'push');
    expect(text()).toContain('{name: case3, when: push}');
    await user.click(within(panel).getByRole('button', { name: 'Remove case 1' }));
    expect(text()).not.toContain('name: manual');
  });

  it('only shows for viewers, and shows problems and values it cannot edit', () => {
    const diagnostics: Diagnostic[] = [
      { severity: 'Error', code: 'AC1', message: 'There is no such image.', path: 'jobs.build.image' },
    ];
    render(
      <Harness
        subject={{ id: 'build' }}
        readOnly
        diagnostics={diagnostics}
        initial={file.replace('    image: alpine:3.22\n', '    image: alpine:3.22\n    env: ${{ vars.ENV }}\n')}
      />,
    );
    const panel = screen.getByRole('complementary', { name: 'Settings of build' });
    expect(within(panel).getByLabelText('Image')).toHaveAttribute('readonly');
    expect(within(panel).getByText('There is no such image.')).toBeInTheDocument();
    expect(within(panel).queryByRole('button', { name: 'Add step' })).not.toBeInTheDocument();
    fireEvent.click(within(panel).getByRole('button', { name: /^Environment/ }));
    expect(within(panel).getByText('${{ vars.ENV }}')).toBeInTheDocument();
  });

  it('closes with Esc, but not when Esc only ends adding a name', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn<() => void>();
    render(
      <SidePanel
        subject={{ id: 'build' }}
        text={file}
        graph={graphFromYaml(file).graph ?? { triggers: [], jobs: [], edges: [], positions: {} }}
        nodeTypes={[]}
        diagnostics={[]}
        edits={{ current: () => file, apply: vi.fn<PanelEdits['apply']>(), remove: vi.fn<(id: string) => void>() }}
        onClose={onClose}
      />,
    );
    const panel = screen.getByRole('complementary');
    await user.click(within(panel).getByRole('button', { name: /^Environment/ }));
    await user.click(within(panel).getByRole('button', { name: 'Add name' }));
    await user.keyboard('{Escape}');
    expect(onClose).not.toHaveBeenCalled();
    await user.click(within(panel).getByLabelText('Image'));
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
    await user.click(within(panel).getByRole('button', { name: 'Widen the panel' }));
    expect(within(panel).getByRole('button', { name: 'Narrow the panel' })).toBeInTheDocument();
    await user.click(within(panel).getByRole('button', { name: 'Delete build' }));
  });

  it('says when several cards or none are there', () => {
    const { rerender } = render(<Harness subject={{ count: 2 }} />);
    expect(screen.getByText('Choose one card to see its settings.')).toBeInTheDocument();
    rerender(<Harness subject={{ id: 'gone' }} />);
  });
});

describe('the automation panel', () => {
  it('sets what starts the automation and its settings', async () => {
    const user = userEvent.setup();
    render(<Harness subject={{ id: 'trigger' }} />);
    const panel = screen.getByRole('complementary', { name: 'Settings of Automation' });

    await user.click(within(panel).getByLabelText('By a webhook'));
    expect(text()).toContain('  manual: true\n  webhook: true\n');
    await user.click(within(panel).getByRole('button', { name: 'Add a schedule' }));
    expect(text()).toContain('  schedule:\n    - cron: 0 6 * * *\n');
    await user.selectOptions(within(panel).getByLabelText('Schedule 1'), 'interval');
    expect(text()).toContain('    - interval: 1h\n');
    await user.click(within(panel).getByLabelText('On pushes to the repository'));
    // Written as a block, in the file's own style of lists.
    expect(text()).toContain('  push:\n    branches:\n    - main\n');
    await user.click(within(panel).getByLabelText('When another automation finishes'));
    await user.type(within(panel).getByLabelText('Automation'), 'nightly');
    expect(text()).toContain('  automation_completed:\n    automation: nightly\n');

    await user.selectOptions(within(panel).getByLabelText('Runs at the same time'), 'single');
    expect(text()).toContain('concurrency: single\n');
    await user.click(within(panel).getByLabelText('By hand'));
    await user.click(within(panel).getByLabelText('By a webhook'));
    await user.click(within(panel).getByLabelText('On pushes to the repository'));
    await user.click(within(panel).getByLabelText('When another automation finishes'));
    await user.click(within(panel).getByRole('button', { name: 'Remove schedule 1' }));
    expect(text()).not.toContain('triggers');
    expect(within(panel).getByText(/Nothing starts it/)).toBeInTheDocument();
  });
});
