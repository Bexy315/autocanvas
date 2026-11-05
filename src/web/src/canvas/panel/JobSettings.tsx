import { ArrowUp } from 'lucide-react';
import { useState } from 'react';
import type { Graph, JobData } from '../model';
import { renameJob, renameProblem, setNeeds } from '../operations';
import {
  isMapping,
  isMappingList,
  isPlainMap,
  isTextList,
  positionKey,
  problemsAt,
  problemsUnder,
  type Mapping,
  type PanelFile,
} from '../panelModel';
import type { Path } from '../yamlEdit';
import {
  AddButton,
  Choice,
  KeyValueList,
  NumberInput,
  RemoveButton,
  Section,
  TagList,
  TextInput,
  Toggle,
  YamlOnly,
} from './fields';
import { listEditor, usePanel, type PanelEdits } from './panelContext';

type Set = (path: Path, value: unknown, summary: string) => void;

interface SettingsProps {
  id: string;
  file: PanelFile;
  graph: Graph;
  edits: PanelEdits | undefined;
  onSet: Set;
}

const text = (value: unknown) => (typeof value === 'string' ? value : '');

/** Id, name and condition: what every kind of job has. */
export function GeneralSection({ id, file, edits, onSet }: Omit<SettingsProps, 'graph'>) {
  const { diagnostics } = usePanel();
  const base = ['jobs', id];
  const [draft, setDraft] = useState(id);
  const problem = edits && draft !== id ? renameProblem(edits.current(), id, draft) : null;
  const rename = () => {
    if (!edits || draft === id || problem !== null) {
      return;
    }
    edits.apply(renameJob(edits.current(), id, draft), { step: true, announce: true });
  };
  return (
    <Section title="General" open summary={id} problems={problemsUnder(diagnostics, [...base, 'name'])}>
      <TextInput
        label="Id"
        value={draft}
        mono
        hint="Other jobs and expressions name the job by it; they follow when it changes."
        problems={problem ? [problem] : []}
        onValue={setDraft}
        onCommit={rename}
      />
      <TextInput
        label="Name"
        value={text(file.get([...base, 'name']))}
        empty={`the id, ${id}`}
        problems={problemsAt(diagnostics, [...base, 'name'])}
        onValue={(value) => onSet([...base, 'name'], value, `Named ${id}`)}
      />
      <TextInput
        label="Run only if"
        value={text(file.get([...base, 'if']))}
        empty="always"
        example="${{ trigger.branch == 'main' }}"
        mono
        problems={problemsAt(diagnostics, [...base, 'if'])}
        onValue={(value) => onSet([...base, 'if'], value, `Changed the condition of ${id}`)}
      />
    </Section>
  );
}

interface Need {
  job: string;
  /** `success`, `failure`, `always`, or `case:NAME` for a switch's case. */
  when: string;
}

function readNeed(entry: unknown): Need | null {
  if (typeof entry === 'string') {
    return { job: entry, when: 'success' };
  }
  if (!isMapping(entry) || typeof entry['job'] !== 'string') {
    return null;
  }
  if (typeof entry['case'] === 'string') {
    return { job: entry['job'], when: `case:${entry['case']}` };
  }
  return { job: entry['job'], when: typeof entry['on'] === 'string' ? entry['on'] : 'success' };
}

function writeNeed(need: Need): unknown {
  return need.when.startsWith('case:') ? { job: need.job, case: need.when.slice(5) } : { job: need.job, on: need.when };
}

const outcomes = [
  { value: 'success', label: 'when it succeeds' },
  { value: 'failure', label: 'when it fails' },
  { value: 'always', label: 'whatever happens' },
];

function whenOptions(source: JobData | undefined) {
  return source?.kind === 'switch'
    ? source.cases.map((entry) => ({ value: `case:${entry.name}`, label: `on case ${entry.name}` }))
    : outcomes;
}

/** What the job waits for, one row per job: the keyboard's way to draw lines on the canvas. */
export function NeedsSection({ id, file, graph, edits }: Omit<SettingsProps, 'onSet'>) {
  const { diagnostics, readOnly } = usePanel();
  const path = ['jobs', id, 'needs'];
  const value = file.get(path);
  if (graph.jobs.find((job) => job.id === id)?.isFinally) {
    return null;
  }
  if (value !== undefined && (!Array.isArray(value) || file.shared(path))) {
    return (
      <Section title="Needs" open>
        <YamlOnly label="Needs" source={file.source(path)} />
      </Section>
    );
  }
  const needs = (value ?? []).map(readNeed);
  const others = graph.jobs.filter((job) => job.id !== id && !job.isFinally);
  const write = (next: (Need | null)[]) =>
    edits?.apply(
      setNeeds(
        edits.current(),
        id,
        next.map((need, index) => (need ? writeNeed(need) : (value as unknown[])[index])),
      ),
      { step: true, announce: true },
    );
  const unused = others.find((job) => !needs.some((need) => need?.job === job.id));

  return (
    <Section
      title="Needs"
      summary={needs.length === 0 ? 'starts with the run' : needs.map((need) => need?.job ?? '…').join(', ')}
      problems={problemsUnder(diagnostics, path)}
    >
      {needs.length === 0 && <p className="text-xs text-muted">The job starts when the run starts.</p>}
      {needs.map((need, index) =>
        need ? (
          <div key={positionKey(index)} className="flex items-end gap-1">
            <div className="flex-1">
              <Choice
                label={`Waits for job ${index + 1}`}
                value={need.job}
                options={others.map((job) => ({ value: job.id, label: job.title }))}
                problems={problemsAt(diagnostics, [...path, index])}
                onValue={(job) =>
                  write(
                    needs.map((other, at) =>
                      at === index
                        ? {
                            job,
                            when: whenOptions(graph.jobs.find((entry) => entry.id === job))[0]?.value ?? 'success',
                          }
                        : other,
                    ),
                  )
                }
              />
            </div>
            <div className="flex-1">
              <Choice
                label="Starts"
                value={need.when}
                options={whenOptions(graph.jobs.find((job) => job.id === need.job))}
                onValue={(when) => write(needs.map((other, at) => (at === index ? { ...need, when } : other)))}
              />
            </div>
            {!readOnly && (
              <RemoveButton
                label={`Stop waiting for ${need.job}`}
                onRemove={() => write(needs.filter((_, at) => at !== index))}
              />
            )}
          </div>
        ) : (
          <YamlOnly key={positionKey(index)} label={`Need ${index + 1}`} source={file.source([...path, index])} />
        ),
      )}
      {!readOnly && unused && (
        <AddButton
          label="Add a job to wait for"
          onAdd={() => write([...needs, { job: unused.id, when: whenOptions(unused)[0]?.value ?? 'success' }])}
        />
      )}
    </Section>
  );
}

/** Where and how a job with steps runs. */
export function StepJobSections(props: SettingsProps) {
  const { id, file, graph, onSet } = props;
  const { diagnostics } = usePanel();
  const base = ['jobs', id];
  const at = (...keys: (string | number)[]): Path => [...base, ...keys];
  const runsOn = file.get(at('runs_on'));
  const labels = typeof runsOn === 'string' ? [runsOn] : isTextList(runsOn) ? runsOn : null;
  const env = file.get(at('env'));
  const job = graph.jobs.find((entry) => entry.id === id);

  return (
    <>
      <Section
        title="Runs on"
        summary={[labels?.join(', ') || 'any runner', text(file.get(at('image')))].filter(Boolean).join(' · ')}
        problems={problemsUnder(diagnostics, at('runs_on')) + problemsUnder(diagnostics, at('image'))}
      >
        {labels === null ? (
          <YamlOnly label="Runner labels" source={file.source(at('runs_on'))} />
        ) : (
          <TagList
            label="Runner labels"
            hint="The job goes to a runner that has all of them."
            values={labels}
            problems={problemsAt(diagnostics, at('runs_on'))}
            onValues={(values) =>
              onSet(at('runs_on'), values.length === 1 ? values[0] : values, `Changed the runner labels of ${id}`)
            }
          />
        )}
        <TextInput
          label="Image"
          value={text(file.get(at('image')))}
          empty="the runner's default image"
          example="alpine:3.22"
          mono
          hint="The container the steps run in."
          problems={problemsAt(diagnostics, at('image'))}
          onValue={(value) => onSet(at('image'), value, `Changed the image of ${id}`)}
        />
      </Section>
      <Section
        title="Environment"
        summary={env === undefined ? 'none' : isPlainMap(env) ? countOf(env, 'variable') : 'in the YAML'}
        problems={problemsUnder(diagnostics, at('env'))}
      >
        {isPlainMap(env) || env === undefined ? (
          <KeyValueList
            label="Variables for every step"
            entries={isPlainMap(env) ? env : {}}
            onEntries={(entries) => onSet(at('env'), entries, `Changed the environment of ${id}`)}
          />
        ) : (
          <YamlOnly label="Environment" source={file.source(at('env'))} />
        )}
      </Section>
      <ServicesSection {...props} />
      <CheckoutSection {...props} />
      <ReportsSection {...props} />
      <Section
        title="Limits"
        summary={limitsSummary(file, base)}
        problems={problemsUnder(diagnostics, at('timeout_minutes')) + problemsUnder(diagnostics, at('retry'))}
      >
        <NumberInput
          label="Timeout in minutes"
          value={file.get(at('timeout_minutes'))}
          problems={problemsAt(diagnostics, at('timeout_minutes'))}
          onValue={(value) => onSet(at('timeout_minutes'), value, `Changed the timeout of ${id}`)}
        />
        <NumberInput
          label="Attempts in total"
          hint="Runs the job again when it fails, up to this many times (2 to 10)."
          value={file.get(at('retry', 'max_attempts'))}
          problems={problemsAt(diagnostics, at('retry', 'max_attempts'))}
          onValue={(value) => onSet(at('retry', 'max_attempts'), value, `Changed the attempts of ${id}`)}
        />
        <NumberInput
          label="Seconds between attempts"
          value={file.get(at('retry', 'delay_seconds'))}
          problems={problemsAt(diagnostics, at('retry', 'delay_seconds'))}
          onValue={(value) => onSet(at('retry', 'delay_seconds'), value, `Changed the retry delay of ${id}`)}
        />
      </Section>
      <Section title="Outputs" summary={countOf(file.get(at('outputs')), 'output')}>
        {isPlainMap(file.get(at('outputs'))) || file.get(at('outputs')) === undefined ? (
          <KeyValueList
            label="Published to later jobs"
            hint="Name and expression, such as ${{ steps.build.outputs.version }}."
            entries={(file.get(at('outputs')) as Record<string, string> | undefined) ?? {}}
            onEntries={(entries) => onSet(at('outputs'), entries, `Changed the outputs of ${id}`)}
          />
        ) : (
          <YamlOnly label="Outputs" source={file.source(at('outputs'))} />
        )}
      </Section>
      {job?.isFinally && (
        <Section title="Finally" open>
          <Toggle
            label="Also run when the run is cancelled"
            checked={file.get(at('on_cancel')) === true}
            onChecked={(checked) => onSet(at('on_cancel'), checked || undefined, `Changed ${id}`)}
          />
        </Section>
      )}
    </>
  );
}

/** "none", "1 report", "3 reports": every folded section says what it holds. */
function countOf(value: unknown, noun: string): string {
  const count = isMapping(value) ? Object.keys(value).length : Array.isArray(value) ? value.length : 0;
  return count === 0 ? 'none' : `${count} ${noun}${count === 1 ? '' : 's'}`;
}

function limitsSummary(file: PanelFile, base: Path): string {
  const timeout = file.get([...base, 'timeout_minutes']);
  const attempts = file.get([...base, 'retry', 'max_attempts']);
  return (
    [
      timeout === undefined ? null : `${String(timeout)} min`,
      attempts === undefined ? null : `${String(attempts)} attempts`,
    ]
      .filter(Boolean)
      .join(' · ') || '60 min'
  );
}

const presets = ['postgres', 'mysql', 'redis'].map((preset) => ({ value: preset, label: preset }));

function ServicesSection({ id, file, onSet }: SettingsProps) {
  const { diagnostics, readOnly } = usePanel();
  const path = ['jobs', id, 'services'];
  const services = file.get(path);
  const [naming, setNaming] = useState(false);
  if (services !== undefined && (!isMapping(services) || file.shared(path))) {
    return (
      <Section title="Services">
        <YamlOnly label="Services" source={file.source(path)} />
      </Section>
    );
  }
  const entries = Object.entries(services ?? {});
  return (
    <Section
      title="Services"
      summary={entries.map(([name]) => name).join(', ') || 'none'}
      problems={problemsUnder(diagnostics, path)}
    >
      <p className="text-xs text-muted">Containers next to the job, reachable under their name as host name.</p>
      {entries.map(([name, service]) => {
        const at = (key: string): Path => [...path, name, key];
        const preset = isMapping(service) && typeof service['image'] !== 'string';
        return isMapping(service) ? (
          <fieldset key={name} className="flex flex-col gap-2 rounded-control border border-line p-2.5">
            <legend className="px-1 font-mono text-xs font-medium">{name}</legend>
            <div className="flex items-end gap-1">
              <div className="flex-1">
                <Choice
                  label="From"
                  value={preset ? 'preset' : 'image'}
                  options={[
                    { value: 'preset', label: 'A preset' },
                    { value: 'image', label: 'An image' },
                  ]}
                  onValue={(from) =>
                    onSet(
                      [...path, name],
                      from === 'preset' ? { preset: 'postgres' } : { image: 'postgres:17' },
                      `Changed the service ${name}`,
                    )
                  }
                />
              </div>
              {!readOnly && (
                <RemoveButton
                  label={`Remove the service ${name}`}
                  onRemove={() => onSet([...path, name], undefined, `Removed the service ${name}`)}
                />
              )}
            </div>
            {preset ? (
              <>
                <Choice
                  label="Preset"
                  value={text(service['preset'])}
                  options={presets}
                  onValue={(value) => onSet(at('preset'), value, `Changed the service ${name}`)}
                />
                <TextInput
                  label="Version"
                  value={service['version'] === undefined ? '' : String(service['version'])}
                  empty="the preset's default"
                  example="17"
                  onValue={(value) => onSet(at('version'), value, `Changed the service ${name}`)}
                />
              </>
            ) : (
              <TextInput
                label="Image"
                value={text(service['image'])}
                mono
                problems={problemsAt(diagnostics, at('image'))}
                onValue={(value) => onSet(at('image'), value, `Changed the service ${name}`)}
              />
            )}
            {isPlainMap(service['env']) || service['env'] === undefined ? (
              <KeyValueList
                label="Environment"
                entries={isPlainMap(service['env']) ? service['env'] : {}}
                onEntries={(value) => onSet(at('env'), value, `Changed the service ${name}`)}
              />
            ) : (
              <YamlOnly label="Environment" source={file.source(at('env'))} />
            )}
            <TextInput
              label="Ready when this succeeds"
              value={text(service['ready'])}
              {...(preset ? { empty: "the preset's own check" } : { example: 'pg_isready' })}
              mono
              onValue={(value) => onSet(at('ready'), value, `Changed the service ${name}`)}
            />
          </fieldset>
        ) : (
          <YamlOnly key={name} label={name} source={file.source([...path, name])} />
        );
      })}
      {!readOnly &&
        (naming ? (
          <input
            // oxlint-disable-next-line jsx-a11y/no-autofocus -- the field appears because "Add a service" was just chosen
            autoFocus
            aria-label="Name of the new service"
            placeholder="db"
            onBlur={(event) => {
              const name = event.target.value.trim();
              if (/^[a-z][a-z0-9_-]*$/.test(name) && !(name in (services ?? {}))) {
                onSet([...path, name], { preset: 'postgres' }, `Added the service ${name}`);
              }
              setNaming(false);
            }}
            onKeyDown={(event) => {
              if (event.key === 'Enter') {
                event.currentTarget.blur();
              } else if (event.key === 'Escape') {
                event.stopPropagation();
                setNaming(false);
              }
            }}
            className="h-8 w-40 rounded-control border border-line-strong bg-surface px-2 font-mono text-xs"
          />
        ) : (
          <AddButton label="Add a service" onAdd={() => setNaming(true)} />
        ))}
    </Section>
  );
}

function CheckoutSection({ id, file, edits, onSet }: SettingsProps) {
  const lists = listEditor(edits);
  const { diagnostics, readOnly } = usePanel();
  const path = ['jobs', id, 'checkout'];
  const checkout = file.get(path);
  if (checkout !== undefined && checkout !== false && (!isMappingList(checkout) || file.shared(path))) {
    return (
      <Section title="Checkout">
        <YamlOnly label="Checkout" source={file.source(path)} />
      </Section>
    );
  }
  const mode = checkout === false ? 'none' : Array.isArray(checkout) ? 'list' : 'default';
  const repositories: readonly Mapping[] = Array.isArray(checkout) ? checkout : [];
  return (
    <Section
      title="Checkout"
      summary={mode === 'none' ? 'nothing' : mode === 'list' ? countOf(repositories, 'repository') : 'its own commit'}
      problems={problemsUnder(diagnostics, path)}
    >
      <Choice
        label="Before the steps"
        value={mode}
        options={[
          { value: 'default', label: "Check out the automation's own repository, if any" },
          { value: 'none', label: 'Check out nothing' },
          { value: 'list', label: 'Check out these repositories' },
        ]}
        onValue={(next) =>
          onSet(
            path,
            next === 'none' ? false : next === 'list' ? [{ repository: 'self' }] : undefined,
            `Changed the checkout of ${id}`,
          )
        }
      />
      {repositories.map((repository, index) => {
        const at = (key: string): Path => [...path, index, key];
        return (
          <fieldset key={positionKey(index)} className="flex flex-col gap-2 rounded-control border border-line p-2.5">
            <legend className="px-1 text-xs font-medium">Repository {index + 1}</legend>
            <div className="flex items-end gap-1">
              <div className="flex-1">
                <TextInput
                  label="Repository"
                  value={text(repository['repository'])}
                  hint="'self', a connected repository's name, or an https URL."
                  mono
                  problems={problemsAt(diagnostics, at('repository'))}
                  onValue={(value) => onSet(at('repository'), value, `Changed the checkout of ${id}`)}
                />
              </div>
              {!readOnly && (
                <RemoveButton
                  label={`Remove repository ${index + 1}`}
                  onRemove={() => lists.remove(path, index, `Changed the checkout of ${id}`)}
                />
              )}
            </div>
            <TextInput
              label="Ref"
              value={text(repository['ref'])}
              empty="the triggering commit, or the default branch"
              mono
              onValue={(value) => onSet(at('ref'), value, `Changed the checkout of ${id}`)}
            />
            <TextInput
              label="Path"
              value={text(repository['path'])}
              empty="the workspace"
              mono
              onValue={(value) => onSet(at('path'), value, `Changed the checkout of ${id}`)}
            />
            <NumberInput
              label="History depth"
              value={repository['depth']}
              empty="1 commit"
              hint="0 fetches the whole history."
              onValue={(value) => onSet(at('depth'), value, `Changed the checkout of ${id}`)}
            />
          </fieldset>
        );
      })}
      {!readOnly && mode === 'list' && (
        <AddButton
          label="Add a repository"
          onAdd={() => lists.add(path, { repository: 'self' }, `Changed the checkout of ${id}`)}
        />
      )}
    </Section>
  );
}

const formats = ['junit', 'trx', 'cobertura'].map((format) => ({ value: format, label: format }));

function ReportsSection({ id, file, edits, onSet }: SettingsProps) {
  const lists = listEditor(edits);
  const { diagnostics, readOnly } = usePanel();
  const path = ['jobs', id, 'reports'];
  const reports = file.get(path);
  if (reports !== undefined && (!isMappingList(reports) || file.shared(path))) {
    return (
      <Section title="Reports">
        <YamlOnly label="Reports" source={file.source(path)} />
      </Section>
    );
  }
  const list: readonly Mapping[] = reports ?? [];
  return (
    <Section title="Reports" summary={countOf(list, 'report')} problems={problemsUnder(diagnostics, path)}>
      <p className="text-xs text-muted">Test and coverage reports collected after the steps, even when one failed.</p>
      {list.map((report, index) => {
        const at = (key: string): Path => [...path, index, key];
        const paths = report['path'];
        return (
          <fieldset key={positionKey(index)} className="flex flex-col gap-2 rounded-control border border-line p-2.5">
            <legend className="px-1 text-xs font-medium">Report {index + 1}</legend>
            <div className="flex items-end gap-1">
              <div className="flex-1">
                <Choice
                  label="Format"
                  value={text(report['format'])}
                  options={formats}
                  onValue={(value) => onSet(at('format'), value, `Changed a report of ${id}`)}
                />
              </div>
              {!readOnly && (
                <RemoveButton
                  label={`Remove report ${index + 1}`}
                  onRemove={() => lists.remove(path, index, `Removed a report of ${id}`)}
                />
              )}
            </div>
            {typeof paths === 'string' || paths === undefined || isTextList(paths) ? (
              <TagList
                label="Files"
                hint="Glob patterns relative to the workspace, such as **/TestResults/*.trx."
                values={typeof paths === 'string' ? [paths] : (paths ?? [])}
                problems={problemsAt(diagnostics, at('path'))}
                onValues={(values) =>
                  onSet(at('path'), values.length === 1 ? values[0] : values, `Changed a report of ${id}`)
                }
              />
            ) : (
              <YamlOnly label="Files" source={file.source(at('path'))} />
            )}
            <TextInput
              label="Label"
              value={text(report['name'])}
              empty={`the format, ${text(report['format'])}`}
              onValue={(value) => onSet(at('name'), value, `Changed a report of ${id}`)}
            />
          </fieldset>
        );
      })}
      {!readOnly && (
        <AddButton
          label="Add a report"
          onAdd={() => lists.add(path, { format: 'junit', path: '**/TEST-*.xml' }, `Added a report to ${id}`)}
        />
      )}
    </Section>
  );
}

/** A switch: its expression and its cases, each the name later jobs wait for. */
export function SwitchSections({ id, file, edits, onSet }: SettingsProps) {
  const lists = listEditor(edits);
  const { diagnostics, readOnly } = usePanel();
  const base = ['jobs', id];
  const path = [...base, 'cases'];
  const cases = file.get(path);
  const list: readonly Mapping[] = isMappingList(cases) ? cases : [];
  const hasDefault = list.some((entry) => entry['default'] === true);
  const writeCase = (index: number, value: Mapping) => onSet([...path, index], value, `Changed a case of ${id}`);
  return (
    <>
      <Section title="Expression" open problems={problemsUnder(diagnostics, [...base, 'expression'])}>
        <TextInput
          label="Expression"
          value={text(file.get([...base, 'expression']))}
          example="${{ trigger.event }}"
          mono
          hint="The first case whose value it equals is taken."
          problems={problemsAt(diagnostics, [...base, 'expression'])}
          onValue={(value) => onSet([...base, 'expression'], value, `Changed the expression of ${id}`)}
        />
      </Section>
      <Section title="Cases" open problems={problemsUnder(diagnostics, path)}>
        {!isMappingList(cases) || file.shared(path) ? (
          <YamlOnly label="Cases" source={file.source(path)} />
        ) : (
          list.map((entry, index) => {
            const isDefault = entry['default'] === true;
            return (
              <fieldset
                key={positionKey(index)}
                className="flex flex-col gap-2 rounded-control border border-line p-2.5"
              >
                <legend className="px-1 text-xs font-medium">Case {index + 1}</legend>
                <div className="flex items-end gap-1">
                  <div className="flex-1">
                    <TextInput
                      label="Name"
                      value={text(entry['name'])}
                      mono
                      hint="Jobs wait for it with this name."
                      problems={problemsAt(diagnostics, [...path, index, 'name'])}
                      onValue={(name) => writeCase(index, { ...entry, name })}
                    />
                  </div>
                  {!readOnly && index > 0 && (
                    <button
                      type="button"
                      aria-label={`Move case ${index + 1} up`}
                      title="Move up"
                      onClick={() => lists.move(path, index, index - 1, `Moved a case of ${id}`)}
                      className="rounded-control p-1.5 text-muted hover:bg-sunken hover:text-ink"
                    >
                      <ArrowUp aria-hidden="true" size={14} />
                    </button>
                  )}
                  {!readOnly && list.length > 1 && (
                    <RemoveButton
                      label={`Remove case ${index + 1}`}
                      onRemove={() => lists.remove(path, index, `Removed a case of ${id}`)}
                    />
                  )}
                </div>
                <Choice
                  label="Taken"
                  value={isDefault ? 'default' : 'when'}
                  options={[
                    { value: 'when', label: 'when the expression equals' },
                    ...(isDefault || !hasDefault ? [{ value: 'default', label: 'when no other case matches' }] : []),
                  ]}
                  onValue={(taken) =>
                    writeCase(
                      index,
                      taken === 'default' ? { name: entry['name'], default: true } : { name: entry['name'], when: '' },
                    )
                  }
                />
                {!isDefault && (
                  <TextInput
                    label="Value"
                    value={entry['when'] === undefined ? '' : String(entry['when'])}
                    problems={problemsAt(diagnostics, [...path, index, 'when'])}
                    onValue={(when) => writeCase(index, { ...entry, when })}
                  />
                )}
              </fieldset>
            );
          })
        )}
        {!readOnly && isMappingList(cases) && (
          <AddButton
            label="Add a case"
            onAdd={() => {
              let number = list.length + 1;
              while (list.some((entry) => entry['name'] === `case${number}`)) {
                number += 1;
              }
              const added = { name: `case${number}`, when: '' };
              const at = hasDefault ? list.findIndex((entry) => entry['default'] === true) : list.length;
              lists.add(path, added, `Added a case to ${id}`, at);
            }}
          />
        )}
      </Section>
    </>
  );
}
