import { CircleCheck, CircleAlert, LoaderCircle, Save, Undo2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { useBlocker } from 'react-router';
import { readSetting, writeSetting } from '../../app/storage';
import { CanvasPane } from '../../canvas/CanvasPane';
import { jobAtLine } from '../../canvas/lines';
import { useCanvasGraph } from '../../canvas/useCanvasGraph';
import { useNodeTypes, useSaveAutomation, type NodeTypeView } from '../../api/automations';
import { api, unwrapWithTag } from '../../api/client';
import { ApiError, type Diagnostic } from '../../api/problem';
import { ConfirmDialog } from '../../dialogs/ConfirmDialog';
import { ConflictDialog, type Theirs } from '../../dialogs/ConflictDialog';
import { automationSchema } from '../../editor/automationSchema';
import { automationCompletions } from '../../editor/completion';
import { SplitView, type ViewMode } from '../../editor/SplitView';
import { useValidation, type ValidationStatus } from '../../editor/useValidation';
import { ViewSwitch } from '../../editor/ViewSwitch';
import { YamlEditor, type YamlEditorHandle } from '../../editor/YamlEditor';
import { hasRole } from '../../lib/roles';
import { Alert, ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { DiagnosticList } from '../../ui/DiagnosticList';
import { useAutomationContext } from './context';

/** The version the edits are based on: what Save sends as If-Match and what Discard returns to. */
interface Base {
  yaml: string;
  version: number;
  tag: string | null;
}

const versionTag = (version: number) => `"${version}"`;
export const viewKey = 'autocanvas.editor.view';
export const splitKey = 'autocanvas.editor.split';

function storedMode(): ViewMode {
  const value = readSetting(viewKey);
  return value === 'canvas' || value === 'yaml' ? value : 'split';
}

function storedRatio(): number {
  const value = Number(readSetting(splitKey));
  return Number.isFinite(value) && value >= 0.2 && value <= 0.8 ? value : 0.5;
}
const noNodeTypes: readonly NodeTypeView[] = [];

/** Editing an automation's file, checked by the server while typing. Viewers read it here. */
export function EditorTab() {
  const { automation, tag, folder } = useAutomationContext();
  const canEdit = hasRole(automation.myRole, 'Editor');
  const current = Number(automation.currentVersion);
  const [base, setBase] = useState<Base>({ yaml: automation.yaml, version: current, tag });
  const [draft, setDraft] = useState(automation.yaml);
  const [message, setMessage] = useState('');
  const [saved, setSaved] = useState<number | null>(null);
  const [saveProblems, setSaveProblems] = useState<Diagnostic[] | null>(null);
  const [discarding, setDiscarding] = useState(false);
  const [conflict, setConflict] = useState<{ theirs: Theirs | null; error: unknown } | null>(null);
  const [mode, setMode] = useState<ViewMode>(storedMode);
  const [ratio, setRatio] = useState(storedRatio);
  const [selectedJob, setSelectedJob] = useState<string | null>(null);
  const editor = useRef<YamlEditorHandle>(null);
  const dirty = draft !== base.yaml;

  // A newer version read while nothing was changed here (e.g. after a refresh) becomes the base.
  // Only newer: a read that started before our own save may still bring the version before it.
  if (!dirty && current > base.version) {
    setBase({ yaml: automation.yaml, version: current, tag });
    setDraft(automation.yaml);
  }

  const validation = useValidation(draft, folder.id, automation.id, canEdit);
  const nodeTypes = useNodeTypes();
  const nodeTypeList = nodeTypes.data ?? noNodeTypes;
  const completions = useMemo(() => automationCompletions(automationSchema, () => nodeTypeList), [nodeTypeList]);
  const save = useSaveAutomation(automation.id);

  const diagnostics = saveProblems ?? validation.result?.diagnostics ?? [];
  const errors = diagnostics.filter((diagnostic) => diagnostic.severity === 'Error').length;
  const canvas = useCanvasGraph(draft, canEdit ? diagnostics : []);

  const chooseMode = (next: ViewMode) => {
    writeSetting(viewKey, next);
    setMode(next);
  };
  const resize = (next: number) => {
    writeSetting(splitKey, String(next));
    setRatio(next);
  };
  // A card chosen on the canvas shows its job in the file, without taking the focus away.
  const showJob = (id: string) => {
    setSelectedJob(id);
    const range = canvas.lines.get(id);
    if (range) {
      editor.current?.revealLine(range.from);
    }
  };
  const blockedReason = !dirty
    ? 'No changes to save.'
    : validation.status === 'invalid'
      ? 'Fix the problems below to save.'
      : null;

  const edit = (yaml: string) => {
    setDraft(yaml);
    setSaved(null);
    setSaveProblems(null);
  };

  const store = (onTopOf: string | null) => {
    if (!dirty) {
      return;
    }
    const sent = draft;
    save.mutate(
      { yaml: sent, message: message.trim() === '' ? null : message.trim(), tag: onTopOf },
      {
        onSuccess: (view) => {
          const version = Number(view.currentVersion);
          setBase({ yaml: sent, version, tag: versionTag(version) });
          setMessage('');
          setSaved(version);
          setConflict(null);
        },
        onError: (error) => {
          if (error instanceof ApiError && error.status === 412) {
            openConflict();
          } else if (error instanceof ApiError && error.problem.diagnostics) {
            setSaveProblems(error.problem.diagnostics);
            setConflict(null);
          }
        },
      },
    );
  };

  const openConflict = () => {
    setConflict({ theirs: null, error: null });
    unwrapWithTag(api.GET('/api/v1/automations/{id}', { params: { path: { id: automation.id } } })).then(
      ({ data, tag: theirTag }) =>
        setConflict({ theirs: { yaml: data.yaml, version: Number(data.currentVersion), tag: theirTag }, error: null }),
      (error: unknown) => setConflict({ theirs: null, error }),
    );
  };

  // Leaving with unsaved changes asks first: links within the app, and closing or reloading the tab.
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      dirty && !save.isPending && currentLocation.pathname !== nextLocation.pathname,
  );
  useEffect(() => {
    if (!dirty) {
      return undefined;
    }
    const warn = (event: BeforeUnloadEvent) => event.preventDefault();
    globalThis.addEventListener('beforeunload', warn);
    return () => globalThis.removeEventListener('beforeunload', warn);
  }, [dirty]);

  return (
    <div className="flex flex-col gap-3">
      {canEdit ? (
        <div className="flex flex-wrap items-center gap-3 rounded-panel border border-line bg-surface px-3 py-2">
          <EditorStatus dirty={dirty} status={validation.status} errors={errors} saved={saved} />
          <label className="flex min-w-48 flex-1 items-center gap-2">
            <span className="sr-only">Message</span>
            <input
              value={message}
              onChange={(event) => setMessage(event.target.value)}
              placeholder="What changed? (optional)"
              disabled={!dirty}
              className="h-8 w-full rounded-control border border-line-strong bg-surface px-2.5 text-sm placeholder:text-muted disabled:opacity-60"
            />
          </label>
          <Button onClick={() => setDiscarding(true)} disabled={!dirty || save.isPending}>
            <Undo2 aria-hidden="true" size={14} />
            Discard
          </Button>
          <Button
            variant="primary"
            busy={save.isPending}
            disabled={blockedReason !== null}
            title={blockedReason ?? 'Save as a new version (Ctrl+S)'}
            onClick={() => store(base.tag)}
          >
            <Save aria-hidden="true" size={14} />
            Save
          </Button>
        </div>
      ) : (
        <Alert tone="info">You can read this automation here. Changing it needs the Editor role on its folder.</Alert>
      )}
      {saved !== null && (
        <Alert tone="ok" title={`Saved version ${saved}`}>
          Runs started from now on use it.
        </Alert>
      )}
      {save.isError &&
        !(save.error instanceof ApiError && (save.error.status === 412 || save.error.problem.diagnostics)) && (
          <ErrorAlert error={save.error} title="The file was not saved" />
        )}

      <div className="flex justify-end">
        <ViewSwitch mode={mode} onMode={chooseMode} />
      </div>
      <div className="h-[68vh] min-h-96 overflow-hidden rounded-panel border border-line">
        <SplitView
          mode={mode}
          ratio={ratio}
          onRatio={resize}
          leftLabel="Canvas"
          rightLabel="File"
          left={
            <CanvasPane
              graph={canvas.graph}
              stale={canvas.stale}
              selectedJob={selectedJob}
              problemsByJob={canvas.problemsByJob}
              onSelectJob={showJob}
            />
          }
          right={
            <YamlEditor
              ref={editor}
              value={draft}
              onSave={() => blockedReason === null && store(base.tag)}
              onCursor={(line) => setSelectedJob(jobAtLine(canvas.lines, line))}
              readOnly={!canEdit}
              diagnostics={canEdit ? diagnostics : []}
              {...(canEdit ? { onChange: edit, completions } : {})}
              label={`File of ${automation.title}`}
            />
          }
        />
      </div>
      {canEdit && (
        <p className="text-xs text-muted">
          Ctrl+Space suggests keys and values, Ctrl+F searches, Ctrl+S saves. Tab indents; press Esc, then Tab, to leave
          the editor.
        </p>
      )}

      {canEdit && (
        <section aria-labelledby="problems" className="flex flex-col gap-2">
          <h2 id="problems" className="font-semibold">
            Problems {diagnostics.length > 0 && <span className="text-muted">({diagnostics.length})</span>}
          </h2>
          {validation.status === 'failed' && (
            <ErrorAlert error={validation.error} title="The file could not be checked" />
          )}
          {diagnostics.length === 0 ? (
            <p className="text-muted">{validation.status === 'checking' ? 'Checking…' : 'No problems found.'}</p>
          ) : (
            <DiagnosticList
              diagnostics={diagnostics}
              label="Problems in the file"
              onSelect={(diagnostic) => editor.current?.focusAt(diagnostic.line, diagnostic.column)}
            />
          )}
        </section>
      )}

      <ConfirmDialog
        title="Discard your changes?"
        open={discarding}
        onOpenChange={setDiscarding}
        confirmLabel="Discard changes"
        busy={false}
        error={null}
        onConfirm={() => {
          edit(base.yaml);
          setDiscarding(false);
        }}
      >
        The editor goes back to version {base.version}. What you typed since is lost.
      </ConfirmDialog>
      <ConflictDialog
        open={conflict !== null}
        mine={draft}
        theirs={conflict?.theirs ?? null}
        loadError={conflict?.error ?? null}
        busy={save.isPending}
        onCancel={() => setConflict(null)}
        onSaveMine={() => store(conflict?.theirs?.tag ?? null)}
        onLoadTheirs={() => {
          const theirs = conflict?.theirs;
          if (theirs) {
            setBase({ yaml: theirs.yaml, version: theirs.version, tag: theirs.tag });
            edit(theirs.yaml);
          }
          setConflict(null);
        }}
      />
      <ConfirmDialog
        title="Leave without saving?"
        open={blocker.state === 'blocked'}
        onOpenChange={(open) => !open && blocker.reset?.()}
        confirmLabel="Leave and lose changes"
        busy={false}
        error={null}
        onConfirm={() => blocker.proceed?.()}
      >
        Your changes to the file are not saved yet. If you leave now, they are lost.
      </ConfirmDialog>
    </div>
  );
}

function EditorStatus({
  dirty,
  status,
  errors,
  saved,
}: {
  dirty: boolean;
  status: ValidationStatus;
  errors: number;
  saved: number | null;
}) {
  const check =
    status === 'checking' ? (
      <span className="flex items-center gap-1.5 text-muted">
        <LoaderCircle aria-hidden="true" size={14} className="animate-spin" />
        Checking…
      </span>
    ) : status === 'invalid' || errors > 0 ? (
      <span className="flex items-center gap-1.5 text-danger">
        <CircleAlert aria-hidden="true" size={14} />
        {errors === 1 ? '1 problem' : `${errors} problems`}
      </span>
    ) : status === 'failed' ? (
      <span className="text-warn">Not checked</span>
    ) : (
      <span className="flex items-center gap-1.5 text-ok">
        <CircleCheck aria-hidden="true" size={14} />
        No problems
      </span>
    );

  return (
    <output aria-live="polite" className="flex items-center gap-3 text-sm">
      <span className={dirty ? 'font-medium text-ink' : 'text-muted'}>
        {dirty ? 'Unsaved changes' : saved !== null ? 'Saved' : 'No changes'}
      </span>
      {check}
    </output>
  );
}
