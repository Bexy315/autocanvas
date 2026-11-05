import { autocompletion, completionKeymap, startCompletion, type CompletionSource } from '@codemirror/autocomplete';
import { defaultKeymap, history, historyKeymap, indentWithTab, redo, undo } from '@codemirror/commands';
import { yaml } from '@codemirror/lang-yaml';
import { bracketMatching, foldGutter, foldKeymap, indentOnInput, syntaxHighlighting } from '@codemirror/language';
import { lintGutter, lintKeymap, setDiagnostics } from '@codemirror/lint';
import { highlightSelectionMatches, openSearchPanel, searchKeymap } from '@codemirror/search';
import { Compartment, EditorState } from '@codemirror/state';
import {
  drawSelection,
  EditorView,
  highlightActiveLine,
  highlightActiveLineGutter,
  keymap,
  lineNumbers,
} from '@codemirror/view';
import { useEffect, useImperativeHandle, useLayoutEffect, useRef, type Ref } from 'react';
import type { Diagnostic } from '../api/problem';
import { offsetOf, toEditorDiagnostics } from './diagnostics';
import { editorTheme, yamlHighlight } from './editorTheme';
import { smallestChange } from './smallestChange';

export interface YamlEditorHandle {
  /** Moves the cursor to a line and column (1-based) and focuses the editor. */
  focusAt: (line: number | string | null | undefined, column: number | string | null | undefined) => void;
  /** Puts the cursor at the start of a line and scrolls it into view, leaving the focus where it is. */
  revealLine: (line: number) => void;
  /**
   * Replaces the text with `text`, changing only the part that differs: how changes made on the
   * canvas reach the file. Each is one step of the undo history; with `typing`, changes that follow
   * each other closely join one step, as typed characters do (a field of the side panel).
   */
  apply: (text: string, options?: { typing?: boolean }) => void;
  undo: () => void;
  redo: () => void;
  /** The text as it is now, also before React has seen the latest change. */
  text: () => string;
  /** Opens the search bar (Ctrl+F inside the editor does the same) and focuses it. */
  search: () => void;
}

export interface YamlEditorProps {
  value: string;
  onChange?: (value: string) => void;
  /** The line (1-based) the cursor is on, whenever it moves. */
  onCursor?: (line: number) => void;
  /** Ctrl+S / Cmd+S inside the editor. */
  onSave?: () => void;
  readOnly?: boolean;
  diagnostics?: readonly Diagnostic[];
  completions?: CompletionSource;
  /** Accessible name of the text area. */
  label: string;
  ref?: Ref<YamlEditorHandle>;
}

const noDiagnostics: readonly Diagnostic[] = [];

/**
 * A YAML editor (CodeMirror 6). Tab indents; Esc then Tab leaves the editor, so the keyboard is
 * never trapped. The document follows `value`; typing reports through `onChange`.
 */
export function YamlEditor({
  value,
  onChange,
  onSave,
  onCursor,
  readOnly = false,
  diagnostics = noDiagnostics,
  completions,
  label,
  ref,
}: YamlEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  // The editor is made once; it calls the latest callbacks through this.
  const callbacks = useRef({ onChange, onSave, onCursor, completions });
  const editable = useRef(new Compartment());
  useLayoutEffect(() => {
    callbacks.current = { onChange, onSave, onCursor, completions };
  });

  // The editor is made once; later changes reach it as transactions below.
  useEffect(() => {
    if (!host.current) {
      return undefined;
    }
    const created = new EditorView({
      parent: host.current,
      state: EditorState.create({
        doc: value,
        extensions: [
          lineNumbers(),
          highlightActiveLineGutter(),
          foldGutter(),
          lintGutter(),
          history(),
          drawSelection(),
          indentOnInput(),
          bracketMatching(),
          highlightActiveLine(),
          highlightSelectionMatches(),
          yaml(),
          syntaxHighlighting(yamlHighlight),
          editorTheme,
          EditorState.tabSize.of(2),
          editable.current.of([EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly)]),
          autocompletion({
            override: [(context) => callbacks.current.completions?.(context) ?? null],
            icons: false,
          }),
          keymap.of([
            {
              key: 'Mod-s',
              preventDefault: true,
              run: () => {
                callbacks.current.onSave?.();
                return true;
              },
            },
            // Ctrl+Space is taken by the system or the browser in places (input method switching), so
            // Ctrl+I suggests too, as in VS Code.
            { key: 'Mod-i', preventDefault: true, run: startCompletion },
            ...completionKeymap,
            ...defaultKeymap,
            ...historyKeymap,
            ...searchKeymap,
            ...foldKeymap,
            ...lintKeymap,
            indentWithTab,
          ]),
          EditorView.contentAttributes.of({ 'aria-label': label }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged) {
              callbacks.current.onChange?.(update.state.doc.toString());
            }
            if (update.docChanged || update.selectionSet) {
              callbacks.current.onCursor?.(update.state.doc.lineAt(update.state.selection.main.head).number);
            }
          }),
        ],
      }),
    });
    view.current = created;
    return () => {
      created.destroy();
      view.current = null;
    };
    // Made once per mount: value, readOnly and diagnostics follow through the effects below.
    // oxlint-disable-next-line react/exhaustive-deps
  }, []);

  useEffect(() => {
    const current = view.current;
    if (current && current.state.doc.toString() !== value) {
      current.dispatch({ changes: { from: 0, to: current.state.doc.length, insert: value } });
    }
  }, [value]);

  useEffect(() => {
    view.current?.dispatch({
      effects: editable.current.reconfigure([EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly)]),
    });
  }, [readOnly]);

  useEffect(() => {
    const current = view.current;
    if (current) {
      current.dispatch(setDiagnostics(current.state, toEditorDiagnostics(current.state.doc, diagnostics)));
    }
  }, [diagnostics, value]);

  useImperativeHandle(ref, () => ({
    focusAt: (line, column) => {
      const current = view.current;
      if (!current) {
        return;
      }
      const at = offsetOf(current.state.doc, line, column);
      current.dispatch({ selection: { anchor: at }, scrollIntoView: true });
      current.focus();
    },
    revealLine: (line) => {
      const current = view.current;
      if (!current) {
        return;
      }
      const at = offsetOf(current.state.doc, line, 1);
      current.dispatch({ selection: { anchor: at }, effects: EditorView.scrollIntoView(at, { y: 'center' }) });
    },
    apply: (text, options) => {
      const current = view.current;
      if (current) {
        current.dispatch({
          changes: smallestChange(current.state.doc.toString(), text),
          // The history joins `input.type` events close in time and place; `input.canvas` stays apart.
          userEvent: options?.typing ? 'input.type.panel' : 'input.canvas',
        });
      }
    },
    text: () => view.current?.state.doc.toString() ?? value,
    undo: () => {
      if (view.current) {
        undo(view.current);
      }
    },
    redo: () => {
      if (view.current) {
        redo(view.current);
      }
    },
    search: () => {
      if (view.current) {
        openSearchPanel(view.current);
      }
    },
  }));

  return <div ref={host} className="h-full min-h-0" />;
}
