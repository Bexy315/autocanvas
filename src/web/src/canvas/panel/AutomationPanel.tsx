/**
 * The side panel for the trigger card: what starts the automation, and the settings of the whole
 * file next to it (harvested in spirit from the origin's TriggerConfigPanel). Webhook addresses,
 * variables of the folder and secrets live on the server, not in the file, and are set up elsewhere.
 */
import {
  isMapping,
  isMappingList,
  isPlainMap,
  isTextList,
  problemsAt,
  problemsUnder,
  positionKey,
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
  TextArea,
  TextInput,
  Toggle,
  YamlOnly,
} from './fields';
import { listEditor, setter, usePanel, type PanelEdits } from './panelContext';

const text = (value: unknown) => (typeof value === 'string' ? value : '');
const at = (...keys: (string | number)[]): Path => ['triggers', ...keys];

export function AutomationPanel({ file, edits }: { file: PanelFile; edits: PanelEdits | undefined }) {
  const { diagnostics } = usePanel();
  const onSet = setter(edits);
  const triggers = file.get(['triggers']);
  const variables = file.get(['variables']);
  return (
    <>
      {triggers !== undefined && (!isMapping(triggers) || file.shared(['triggers'])) ? (
        <Section title="Starts" open>
          <YamlOnly label="Triggers" source={file.source(['triggers'])} />
        </Section>
      ) : (
        <Starts triggers={isMapping(triggers) ? triggers : {}} file={file} onSet={onSet} lists={listEditor(edits)} />
      )}
      <Section
        title="Settings"
        open
        problems={['description', 'timeout_minutes', 'concurrency', 'changed_when'].reduce(
          (sum, key) => sum + problemsUnder(diagnostics, [key]),
          0,
        )}
      >
        <TextArea
          label="Description"
          value={text(file.get(['description']))}
          onValue={(value) => onSet(['description'], value, 'Changed the description')}
        />
        <NumberInput
          label="Timeout of a run in minutes"
          value={file.get(['timeout_minutes'])}
          problems={problemsAt(diagnostics, ['timeout_minutes'])}
          onValue={(value) => onSet(['timeout_minutes'], value, 'Changed the timeout')}
        />
        <Choice
          label="Runs at the same time"
          value={text(file.get(['concurrency']))}
          empty="May overlap (parallel)"
          options={[
            { value: 'parallel', label: 'May overlap (parallel)' },
            { value: 'single', label: 'One at a time (single)' },
          ]}
          onValue={(value) => onSet(['concurrency'], value, 'Changed concurrency')}
        />
        <TextInput
          label="Changed something when"
          value={text(file.get(['changed_when']))}
          example="${{ jobs.check.outputs.changed }}"
          mono
          hint="Successful runs for which this is false are folded together in the history."
          problems={problemsAt(diagnostics, ['changed_when'])}
          onValue={(value) => onSet(['changed_when'], value, 'Changed changed_when')}
        />
      </Section>
      <Section
        title="Variables"
        summary={isPlainMap(variables) ? `${Object.keys(variables).length} variables` : ''}
        problems={problemsUnder(diagnostics, ['variables'])}
      >
        {isPlainMap(variables) || variables === undefined ? (
          <KeyValueList
            label="Variables of this automation"
            hint="Read as ${{ vars.NAME }}; they override the folder's."
            entries={isPlainMap(variables) ? variables : {}}
            onEntries={(entries) => onSet(['variables'], entries, 'Changed the variables')}
          />
        ) : (
          <YamlOnly label="Variables" source={file.source(['variables'])} />
        )}
      </Section>
    </>
  );
}

function Starts({
  triggers,
  file,
  onSet,
  lists,
}: {
  triggers: Mapping;
  file: PanelFile;
  onSet: (path: Path, value: unknown, summary: string) => void;
  lists: ReturnType<typeof listEditor>;
}) {
  const { diagnostics, readOnly } = usePanel();
  const schedule = triggers['schedule'];
  const schedules: readonly Mapping[] = isMappingList(schedule) ? schedule : [];
  const push = triggers['push'];
  const pullRequest = triggers['pull_request'];
  const completed = triggers['automation_completed'];
  const nothing = Object.keys(triggers).length === 0;

  return (
    <Section title="Starts" open problems={problemsUnder(diagnostics, ['triggers'])}>
      {nothing && (
        <p className="text-xs text-muted">
          Nothing starts it: it runs only when started by hand through the API or by another automation.
        </p>
      )}
      <Toggle
        label="By hand"
        hint="From the UI or the API."
        checked={triggers['manual'] === true}
        onChecked={(checked) => onSet(at('manual'), checked || undefined, 'Changed starting by hand')}
      />
      <Toggle
        label="By a webhook"
        hint="A signed HTTP call to the automation's webhook address."
        checked={triggers['webhook'] === true}
        onChecked={(checked) => onSet(at('webhook'), checked || undefined, 'Changed the webhook')}
      />

      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-xs font-medium">On a schedule</legend>
        {schedule !== undefined && (!isMappingList(schedule) || file.shared(at('schedule'))) ? (
          <YamlOnly label="Schedule" source={file.source(at('schedule'))} />
        ) : (
          schedules.map((entry, index) => {
            const interval = typeof entry['interval'] === 'string';
            return (
              <div key={positionKey(index)} className="flex flex-col gap-2 rounded-control border border-line p-2.5">
                <div className="flex items-end gap-1">
                  <div className="flex-1">
                    <Choice
                      label={`Schedule ${index + 1}`}
                      value={interval ? 'interval' : 'cron'}
                      options={[
                        { value: 'cron', label: 'At times (cron)' },
                        { value: 'interval', label: 'Every … (interval)' },
                      ]}
                      onValue={(kind) =>
                        onSet(
                          at('schedule', index),
                          kind === 'interval' ? { interval: '1h' } : { cron: '0 6 * * *' },
                          'Changed a schedule',
                        )
                      }
                    />
                  </div>
                  {!readOnly && (
                    <RemoveButton
                      label={`Remove schedule ${index + 1}`}
                      onRemove={() => lists.remove(at('schedule'), index, 'Removed a schedule')}
                    />
                  )}
                </div>
                {interval ? (
                  <TextInput
                    label="Interval"
                    value={text(entry['interval'])}
                    hint="Such as 30s, 5m, 2h or 1d; at least 10s."
                    mono
                    problems={problemsAt(diagnostics, at('schedule', index, 'interval'))}
                    onValue={(value) => onSet(at('schedule', index, 'interval'), value, 'Changed a schedule')}
                  />
                ) : (
                  <>
                    <TextInput
                      label="Cron"
                      value={text(entry['cron'])}
                      hint="Five fields: minute, hour, day of month, month, day of week."
                      mono
                      problems={problemsAt(diagnostics, at('schedule', index, 'cron'))}
                      onValue={(value) => onSet(at('schedule', index, 'cron'), value, 'Changed a schedule')}
                    />
                    <TextInput
                      label="Time zone"
                      value={text(entry['timezone'])}
                      empty="UTC"
                      problems={problemsAt(diagnostics, at('schedule', index, 'timezone'))}
                      onValue={(value) => onSet(at('schedule', index, 'timezone'), value, 'Changed a schedule')}
                    />
                  </>
                )}
              </div>
            );
          })
        )}
        {!readOnly && (schedule === undefined || isMappingList(schedule)) && (
          <AddButton
            label="Add a schedule"
            onAdd={() => lists.add(at('schedule'), { cron: '0 6 * * *' }, 'Added a schedule')}
          />
        )}
      </fieldset>

      <Toggle
        label="On pushes to the repository"
        hint="For automations that live in a repository."
        checked={push !== undefined}
        onChecked={(checked) => onSet(at('push'), checked ? { branches: ['main'] } : undefined, 'Changed push')}
      />
      {push !== undefined &&
        (isMapping(push) ? (
          <div className="flex flex-col gap-2 pl-6">
            {(['branches', 'tags', 'paths'] as const).map((key) => {
              const value = push[key];
              return value === undefined || isTextList(value) ? (
                <TagList
                  key={key}
                  label={{ branches: 'Branches', tags: 'Tags', paths: 'Changed paths' }[key]}
                  hint={
                    {
                      branches: 'Patterns; empty means all branches.',
                      tags: 'Patterns; empty means tags do not start it.',
                      paths: 'Only when a changed file matches one.',
                    }[key]
                  }
                  values={value ?? []}
                  problems={problemsAt(diagnostics, at('push', key))}
                  onValues={(values) => onSet(at('push', key), values, `Changed push ${key}`)}
                />
              ) : (
                <YamlOnly key={key} label={key} source={file.source(at('push', key))} />
              );
            })}
          </div>
        ) : (
          <YamlOnly label="Push" source={file.source(at('push'))} />
        ))}

      <Toggle
        label="On pull requests"
        hint="For automations that live in a repository; pull requests from forks run without secrets."
        checked={pullRequest !== undefined}
        onChecked={(checked) =>
          onSet(at('pull_request'), checked ? { branches: ['main'] } : undefined, 'Changed pull_request')
        }
      />
      {pullRequest !== undefined &&
        (isMapping(pullRequest) ? (
          <div className="flex flex-col gap-2 pl-6">
            {(['branches', 'paths', 'types'] as const).map((key) => {
              const value = pullRequest[key];
              return value === undefined || isTextList(value) ? (
                <TagList
                  key={key}
                  label={{ branches: 'Target branches', paths: 'Changed paths', types: 'Types' }[key]}
                  hint={
                    {
                      branches: 'Patterns of the branch it goes into; empty means all.',
                      paths: 'Only when a changed file matches one.',
                      types: 'opened, synchronize, reopened, ready_for_review, edited; empty means the first three.',
                    }[key]
                  }
                  values={value ?? []}
                  problems={problemsAt(diagnostics, at('pull_request', key))}
                  onValues={(values) => onSet(at('pull_request', key), values, `Changed pull_request ${key}`)}
                />
              ) : (
                <YamlOnly key={key} label={key} source={file.source(at('pull_request', key))} />
              );
            })}
          </div>
        ) : (
          <YamlOnly label="Pull request" source={file.source(at('pull_request'))} />
        ))}

      <Toggle
        label="When another automation finishes"
        checked={completed !== undefined}
        onChecked={(checked) =>
          onSet(at('automation_completed'), checked ? { automation: '' } : undefined, 'Changed automation_completed')
        }
      />
      {completed !== undefined &&
        (isMapping(completed) ? (
          <div className="flex flex-col gap-2 pl-6">
            <TextInput
              label="Automation"
              value={text(completed['automation'])}
              hint="Its name in the same folder, or its path."
              problems={problemsAt(diagnostics, at('automation_completed', 'automation'))}
              onValue={(value) =>
                onSet(at('automation_completed'), { ...completed, automation: value }, 'Changed automation_completed')
              }
            />
            <Choice
              label="When it"
              value={text(completed['on'])}
              empty="succeeds"
              options={[
                { value: 'failure', label: 'fails' },
                { value: 'always', label: 'finishes, whatever happened' },
              ]}
              onValue={(value) => onSet(at('automation_completed', 'on'), value, 'Changed automation_completed')}
            />
          </div>
        ) : (
          <YamlOnly label="Automation completed" source={file.source(at('automation_completed'))} />
        ))}
    </Section>
  );
}
