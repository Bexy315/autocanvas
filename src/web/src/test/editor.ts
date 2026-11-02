import { EditorView } from '@codemirror/view';
import { screen } from '@testing-library/react';

/** The CodeMirror editor labelled `label`, to type into from tests (jsdom has no real typing for it). */
export async function editorView(label: string): Promise<EditorView> {
  const content = await screen.findByRole('textbox', { name: label });
  const view = EditorView.findFromDOM(content);
  if (!view) {
    throw new Error(`No editor for ${label}.`);
  }
  return view;
}

/** Replaces the whole text, as typing would. */
export function replaceText(view: EditorView, text: string): void {
  view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: text } });
}
