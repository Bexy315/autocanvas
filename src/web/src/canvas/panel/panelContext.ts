import { createContext, useContext } from 'react';
import type { Diagnostic } from '../../api/problem';
import { noReferences, type References } from './references';
import { addListItem, moveListItem, removeListItem, setField, type Change } from '../operations';
import { problemsAt } from '../panelModel';
import type { Path } from '../yamlEdit';

/** What every field in the side panel needs to know: whether it may change, and the file's problems. */
export interface PanelSettings {
  readOnly: boolean;
  diagnostics: readonly Diagnostic[];
  /** The variables and secrets that reach the automation, for inserting them into values. */
  references: References;
}

export const PanelContext = createContext<PanelSettings>({ readOnly: true, diagnostics: [], references: noReferences });

export function usePanel(): PanelSettings {
  return useContext(PanelContext);
}

/** The server's messages about exactly this path. */
export function useProblems(path: Path): string[] {
  return problemsAt(usePanel().diagnostics, path);
}

/** How the panel changes the file; absent for viewers and while the file cannot be read. */
export interface PanelEdits {
  /** The file as the editor has it right now; every change starts from it. */
  current: () => string;
  /** Applies a change; `step` makes it an undo step of its own instead of joining the typing around it. */
  apply: (change: Change | null, options?: { step?: boolean; announce?: boolean }) => void;
  /** Deletes a job, as Delete on the canvas does. */
  remove: (id: string) => void;
}

/** Sets one value of the file from a field, or removes it when the value is empty. */
export function setter(edits: PanelEdits | undefined) {
  return (path: Path, value: unknown, summary: string) => edits?.apply(setField(edits.current(), path, value, summary));
}

/** Adding, removing and moving items of a list from the panel, each a step of the undo history. */
export function listEditor(edits: PanelEdits | undefined) {
  const run = (change: Change | null) => edits?.apply(change, { step: true, announce: true });
  return {
    add: (path: Path, value: unknown, summary: string, index?: number) =>
      edits && run(addListItem(edits.current(), path, value, summary, index)),
    remove: (path: Path, index: number, summary: string) =>
      edits && run(removeListItem(edits.current(), path, index, summary)),
    move: (path: Path, from: number, to: number, summary: string) =>
      edits && run(moveListItem(edits.current(), path, from, to, summary)),
  };
}
