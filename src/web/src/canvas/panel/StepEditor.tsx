import type { NodeTypeView } from '../operations';
import { isMapping, isPlainMap, problemsAt, problemsUnder, type PanelFile } from '../panelModel';
import type { Path } from '../yamlEdit';
import { Choice, KeyValueList, NumberInput, Section, TextArea, TextInput, Toggle, YamlOnly } from './fields';
import { NodeInputs } from './NodeInputs';
import { usePanel } from './panelContext';

type Set = (path: Path, value: unknown, summary: string) => void;

const shells = ['bash', 'sh', 'pwsh', 'cmd'].map((shell) => ({ value: shell, label: shell }));

/** One step of a job: a script, or a step of a node type with its inputs. */
export function StepEditor({
  file,
  path,
  index,
  nodeTypes,
  onSet,
}: {
  file: PanelFile;
  /** The step's path, `jobs.ID.steps[N]`. */
  path: Path;
  index: number;
  nodeTypes: readonly NodeTypeView[];
  onSet: Set;
}) {
  const { diagnostics } = usePanel();
  const step = file.get(path);
  const problems = (at: Path) => problemsAt(diagnostics, at);
  const runProblems = problems([...path, 'run']);
  const nameProblems = problems([...path, 'name']);
  if (!isMapping(step) || file.shared(path)) {
    return (
      <div className="p-4">
        <YamlOnly label={`Step ${index + 1}`} source={file.source(path)} />
      </div>
    );
  }
  const text = (key: string) => (typeof step[key] === 'string' ? step[key] : '');
  const type = typeof step['type'] === 'string' ? step['type'] : null;
  const nodeType = type === null ? null : nodeTypes.find((candidate) => candidate.name === type);
  const at = (key: string): Path => [...path, key];

  return (
    <div className="flex flex-col">
      <div className="flex flex-col gap-3 border-b border-line px-4 pt-3 pb-4">
        <p className="text-xs text-muted">
          Step {index + 1} · {type === null ? 'Script' : type}
        </p>
        {nodeType && <p className="text-sm text-ink">{nodeType.description}</p>}
        <TextInput
          label="Name"
          value={text('name')}
          placeholder={type ?? 'Shown in the run'}
          problems={nameProblems}
          onValue={(value) => onSet(at('name'), value, `Named step ${index + 1}`)}
        />
        {type === null ? (
          <TextArea
            label="Script"
            value={text('run')}
            problems={runProblems}
            hint="Runs in the job's shell; may use expressions such as ${{ vars.NAME }}."
            onValue={(value) => onSet(at('run'), value, `Changed the script of step ${index + 1}`)}
          />
        ) : nodeType ? (
          <NodeInputs nodeType={nodeType} file={file} path={path} onSet={onSet} />
        ) : (
          <YamlOnly label={`Inputs of ${type}`} source={file.source(at('with')) || '(none)'} />
        )}
        {nodeType && nodeType.outputs.length > 0 && (
          <p className="text-xs text-muted">
            Outputs:{' '}
            {nodeType.outputs.map((output) => (
              <code key={output.name} className="mr-1.5 font-mono" title={output.description}>
                {output.name}
              </code>
            ))}
            read as <code className="font-mono">{'${{ steps.ID.outputs.NAME }}'}</code> with an id below.
          </p>
        )}
      </div>
      <Section
        title="More"
        summary={moreSummary(step)}
        problems={problemsUnder(diagnostics, path) - runProblems.length}
      >
        <TextInput
          label="Id"
          value={text('id')}
          hint="For reading this step's outputs in later steps."
          problems={problems(at('id'))}
          mono
          onValue={(value) => onSet(at('id'), value, `Changed the id of step ${index + 1}`)}
        />
        <TextInput
          label="Run only if"
          value={text('if')}
          placeholder="${{ trigger.branch == 'main' }}"
          problems={problems(at('if'))}
          mono
          onValue={(value) => onSet(at('if'), value, `Changed the condition of step ${index + 1}`)}
        />
        {type === null && (
          <>
            <Choice
              label="Shell"
              value={text('shell')}
              options={shells}
              empty="Default (bash, else sh)"
              onValue={(value) => onSet(at('shell'), value, `Changed the shell of step ${index + 1}`)}
            />
            <TextInput
              label="Working directory"
              value={text('working_directory')}
              hint="Relative to the workspace."
              onValue={(value) => onSet(at('working_directory'), value, `Changed the directory of step ${index + 1}`)}
            />
          </>
        )}
        {isPlainMap(step['env']) || step['env'] === undefined ? (
          <KeyValueList
            label="Environment"
            entries={isPlainMap(step['env']) ? step['env'] : {}}
            onEntries={(entries) => onSet(at('env'), entries, `Changed the environment of step ${index + 1}`)}
          />
        ) : (
          <YamlOnly label="Environment" source={file.source(at('env'))} />
        )}
        <Toggle
          label="Continue when this step fails"
          checked={step['continue_on_error'] === true}
          onChecked={(checked) => onSet(at('continue_on_error'), checked || undefined, `Changed step ${index + 1}`)}
        />
        <NumberInput
          label="Timeout in minutes"
          value={step['timeout_minutes']}
          problems={problems(at('timeout_minutes'))}
          onValue={(value) => onSet(at('timeout_minutes'), value, `Changed the timeout of step ${index + 1}`)}
        />
        {type !== null &&
          (isPlainMap(step['outputs']) || step['outputs'] === undefined ? (
            <KeyValueList
              label="Extra outputs"
              hint="Name and expression."
              entries={isPlainMap(step['outputs']) ? step['outputs'] : {}}
              onEntries={(entries) => onSet(at('outputs'), entries, `Changed the outputs of step ${index + 1}`)}
            />
          ) : (
            <YamlOnly label="Extra outputs" source={file.source(at('outputs'))} />
          ))}
      </Section>
    </div>
  );
}

function moreSummary(step: Readonly<Record<string, unknown>>): string {
  const parts = [
    typeof step['id'] === 'string' ? `id ${step['id']}` : null,
    step['if'] === undefined ? null : 'condition',
    step['env'] === undefined ? null : 'environment',
    step['continue_on_error'] === true ? 'continues on error' : null,
    step['timeout_minutes'] === undefined ? null : `${String(step['timeout_minutes'])} min`,
  ];
  return parts.filter(Boolean).join(' · ');
}
