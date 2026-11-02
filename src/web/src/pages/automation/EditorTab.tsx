import { ArrowLeft, ChevronRight, CircleCheck, CircleAlert, LoaderCircle, Save, Undo2 } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';
import { Link, useBlocker } from 'react-router';
import { readSetting, writeSetting } from '../../app/storage';
import type { CanvasEdits } from '../../canvas/AutomationCanvas';
import { CanvasPane } from '../../canvas/CanvasPane';
import { jobAtLine, jobLines } from '../../canvas/lines';
import {
  addJob,
  connect,
  connectionProblem,
  deleteJobs,
  disconnect,
  movePositions,
  pinUnplaced,
  tidy,
  type Change,
} from '../../canvas/operations';
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
import { ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { DiagnosticList } from '../../ui/DiagnosticList';
import { useAutomationContext } from './context';
import { SaveDialog } from './SaveDialog';

/** The version the edits are based on: what Save sends as If-Match and what Discard returns to. */
interface Base {
  yaml: string;
  version: number;
  tag: string | null;
}

const versionTag = (version: number) => `"${version}"`;
export const viewKey = 'autocanvas.editor.view';
export const splitKey = 'autocanvas.editor.split';
export const problemsKey = 'autocanvas.editor.problems';

function storedMode(): ViewMode {
  const value = readSetting(viewKey);
  return value === 'canvas' || value === 'yaml' ? value : 'split';
}

function storedRatio(): number {
  const value = Number(readSetting(splitKey));
  return Number.isFinite(value) && value >= 0.2 && value <= 0.8 ? value : 0.5;
}
const noNodeTypes: readonly NodeTypeView[] = [];

/**
 * Editing an automation's file, checked by the server while typing; viewers read it here. It fills
 * the whole window (the app's sidebar and folder tree are left out), as the canvas needs the room.
 */
export function EditorTab() {
  const { automation, tag, folder } = useAutomationContext();
  const canEdit = hasRole(automation.myRole, 'Editor');
  const current = Number(automation.currentVersion);
  const [base, setBase] = useState<Base>({ yaml: automation.yaml, version: current, tag });
  const [draft, setDraft] = useState(automation.yaml);
  // Saving asks for a message first (SaveDialog); `message` is the one the last attempt was sent with.
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const [problemsOpen, setProblemsOpen] = useState(() => readSetting(problemsKey) !== 'closed');
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
  // The last text the canvas made: the canvas follows it at once instead of after typing pauses.
  const [fromCanvas, setFromCanvas] = useState<string | null>(null);
  const canvas = useCanvasGraph(draft, canEdit ? diagnostics : [], fromCanvas);

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

  // Changes made on the canvas: each works on the text as the editor has it right now (two can
  // come in one event, such as deleting a card and its lines) and goes into the editor as one step
  // of its undo history.
  const [announcement, setAnnouncement] = useState('');
  const [tidied, setTidied] = useState(0);
  const currentText = () => editor.current?.text() ?? draft;
  // Cards keep the place they are shown at once something changes on the canvas (pinUnplaced).
  const pinnedText = () => pinUnplaced(currentText());
  const applyChange = (change: Change | null) => {
    if (!change) {
      return;
    }
    editor.current?.apply(change.text);
    setFromCanvas(change.text);
    setAnnouncement(change.summary);
    if (change.focus) {
      setSelectedJob(change.focus);
      const range = jobLines(change.text).get(change.focus);
      if (range) {
        editor.current?.revealLine(range.from);
      }
    }
  };
  const edits: CanvasEdits = {
    move: (positions) => applyChange(movePositions(pinnedText(), positions)),
    add: (kind, at) => applyChange(addJob(pinnedText(), kind, at)),
    connect: ({ source, handle, target }) => applyChange(connect(pinnedText(), source, handle, target)),
    canConnect: (source, target) => connectionProblem(currentText(), source, target) === null,
    disconnect: (lines) => {
      let text = pinnedText();
      let last: Change | null = null;
      for (const line of lines) {
        const change = disconnect(text, line.source, line.handle, line.target);
        if (change) {
          text = change.text;
          last = change;
        }
      }
      applyChange(last && lines.length > 1 ? { text, summary: `Disconnected ${lines.length} lines` } : last);
    },
    remove: (ids) => applyChange(deleteJobs(pinnedText(), ids)),
    undo: () => {
      editor.current?.undo();
      setFromCanvas(currentText());
    },
    redo: () => {
      editor.current?.redo();
      setFromCanvas(currentText());
    },
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

  const store = (onTopOf: string | null, text: string) => {
    if (!dirty) {
      return;
    }
    const sent = draft;
    setMessage(text);
    save.mutate(
      { yaml: sent, message: text === '' ? null : text, tag: onTopOf },
      {
        onSuccess: (view) => {
          const version = Number(view.currentVersion);
          setBase({ yaml: sent, version, tag: versionTag(version) });
          setMessage('');
          setSaved(version);
          setSaving(false);
          setConflict(null);
        },
        onError: (error) => {
          if (error instanceof ApiError && error.status === 412) {
            setSaving(false);
            openConflict();
          } else if (error instanceof ApiError && error.problem.diagnostics) {
            setSaveProblems(error.problem.diagnostics);
            showProblems(true);
            setSaving(false);
            setConflict(null);
          }
        },
      },
    );
  };

  const showProblems = (open: boolean) => {
    writeSetting(problemsKey, open ? 'open' : 'closed');
    setProblemsOpen(open);
  };

  // Ctrl+S anywhere on the page (canvas, bar or file) asks to save; the file's own Ctrl+S does the
  // same and marks the key as handled.
  const askToSave = () => {
    if (dirty) {
      save.reset();
      setSaving(true);
    }
  };
  useEffect(() => {
    if (!canEdit) {
      return undefined;
    }
    const onKey = (event: globalThis.KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && !event.altKey && event.key.toLowerCase() === 's') {
        if (!event.defaultPrevented && dirty) {
          save.reset();
          setSaving(true);
        }
        // Never the browser's "Save page as".
        event.preventDefault();
      }
    };
    globalThis.addEventListener('keydown', onKey);
    return () => globalThis.removeEventListener('keydown', onKey);
  }, [canEdit, dirty, save]);

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
    <div className="flex h-full flex-col bg-canvas">
      <header className="flex min-h-12 shrink-0 flex-wrap items-center gap-x-3 gap-y-2 border-b border-line bg-surface px-3 py-2">
        <Link
          to={`/automations/${automation.id}`}
          aria-label={`Back to ${automation.title}`}
          className="flex min-w-0 items-center gap-2 rounded-control px-1.5 py-1 hover:bg-sunken"
        >
          <ArrowLeft aria-hidden="true" size={16} className="shrink-0" />
          <span className="truncate font-semibold">{automation.title}</span>
        </Link>
        <span className="truncate text-xs text-muted">
          {folder.name} · Version {current}
        </span>
        <div className="ml-auto flex flex-wrap items-center gap-3">
          <ViewSwitch mode={mode} onMode={chooseMode} />
          {canEdit ? (
            <>
              <EditorStatus dirty={dirty} status={validation.status} errors={errors} saved={saved} />
              <Button onClick={() => setDiscarding(true)} disabled={!dirty || save.isPending}>
                <Undo2 aria-hidden="true" size={14} />
                Discard
              </Button>
              <Button
                variant="primary"
                disabled={!dirty}
                title={dirty ? 'Save as a new version (Ctrl+S)' : 'No changes to save.'}
                onClick={askToSave}
              >
                <Save aria-hidden="true" size={14} />
                Save
              </Button>
            </>
          ) : (
            <span className="text-sm text-muted">Read only: changing it needs the Editor role on its folder.</span>
          )}
        </div>
      </header>
      {!saving &&
        save.isError &&
        !(save.error instanceof ApiError && (save.error.status === 412 || save.error.problem.diagnostics)) && (
          <div className="shrink-0 px-3 pt-3">
            <ErrorAlert error={save.error} title="The file was not saved" />
          </div>
        )}

      <main className="min-h-0 flex-1">
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
              announcement={announcement}
              {...(canEdit
                ? {
                    edits,
                    onTidy: () => {
                      applyChange(tidy(currentText()));
                      setTidied((count) => count + 1);
                    },
                    paletteNodeTypes: nodeTypeList,
                    fitKey: tidied,
                  }
                : {})}
            />
          }
          right={
            <YamlEditor
              ref={editor}
              value={draft}
              onSave={askToSave}
              onCursor={(line) => setSelectedJob(jobAtLine(canvas.lines, line))}
              readOnly={!canEdit}
              diagnostics={canEdit ? diagnostics : []}
              {...(canEdit ? { onChange: edit, completions } : {})}
              label={`File of ${automation.title}`}
            />
          }
        />
      </main>

      {canEdit && (
        <section aria-labelledby="problems" className="shrink-0 border-t border-line bg-surface">
          <div className="flex min-h-9 items-center gap-3 px-3">
            <button
              type="button"
              aria-expanded={problemsOpen}
              aria-controls="problem-list"
              onClick={() => showProblems(!problemsOpen)}
              className="flex items-center gap-1.5 rounded-control px-1 py-0.5 text-sm font-semibold hover:bg-sunken"
            >
              <ChevronRight
                aria-hidden="true"
                size={14}
                className={problemsOpen ? 'rotate-90 transition-transform' : 'transition-transform'}
              />
              <span id="problems">Problems</span>
              {diagnostics.length > 0 && <span className="font-normal text-muted">({diagnostics.length})</span>}
            </button>
            <p className="ml-auto hidden truncate text-xs text-muted md:block">
              Ctrl+Space suggests keys and values, Ctrl+F searches, Ctrl+S saves. Tab indents; press Esc, then Tab, to
              leave the editor.
            </p>
          </div>
          {problemsOpen && (
            <div id="problem-list" className="flex max-h-[28vh] flex-col gap-2 overflow-auto px-3 pb-3">
              {validation.status === 'failed' && (
                <ErrorAlert error={validation.error} title="The file could not be checked" />
              )}
              {diagnostics.length === 0 ? (
                <p className="text-sm text-muted">
                  {validation.status === 'checking' ? 'Checking…' : 'No problems found.'}
                </p>
              ) : (
                <DiagnosticList
                  diagnostics={diagnostics}
                  label="Problems in the file"
                  onSelect={(diagnostic) => editor.current?.focusAt(diagnostic.line, diagnostic.column)}
                />
              )}
            </div>
          )}
        </section>
      )}

      {saving && (
        <SaveDialog
          open
          onOpenChange={(open) => !open && !save.isPending && setSaving(false)}
          version={base.version + 1}
          before={base.yaml}
          after={draft}
          blockedReason={blockedReason}
          busy={save.isPending}
          error={save.isError ? save.error : null}
          onSave={(text) => store(base.tag, text)}
        />
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
        onSaveMine={() => store(conflict?.theirs?.tag ?? null, message)}
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
        {dirty ? 'Unsaved changes' : saved !== null ? `Saved version ${saved}` : 'No changes'}
      </span>
      {check}
    </output>
  );
}
