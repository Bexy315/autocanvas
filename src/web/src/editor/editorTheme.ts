import { HighlightStyle } from '@codemirror/language';
import { EditorView } from '@codemirror/view';
import { tags } from '@lezer/highlight';

/**
 * The editor's look from the design tokens (styles/theme.css). Colors are CSS variables, so switching
 * between light and dark needs no new editor.
 */
export const editorTheme = EditorView.theme({
  '&': {
    backgroundColor: 'var(--color-surface)',
    color: 'var(--color-ink)',
    fontSize: '0.8125rem',
    height: '100%',
  },
  '&.cm-focused': { outline: 'none' },
  '.cm-scroller': { fontFamily: 'var(--font-mono)', lineHeight: '1.55' },
  '.cm-content': { caretColor: 'var(--color-accent)', padding: '0.5rem 0' },
  '.cm-cursor, .cm-dropCursor': { borderLeftColor: 'var(--color-accent)' },
  '&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection': {
    backgroundColor: 'var(--color-accent-soft)',
  },
  '.cm-activeLine': { backgroundColor: 'color-mix(in srgb, var(--color-sunken) 60%, transparent)' },
  '.cm-gutters': {
    backgroundColor: 'var(--color-surface)',
    color: 'var(--color-muted)',
    borderRight: '1px solid var(--color-line)',
  },
  '.cm-activeLineGutter': { backgroundColor: 'var(--color-sunken)', color: 'var(--color-ink)' },
  '.cm-tooltip': {
    backgroundColor: 'var(--color-surface)',
    color: 'var(--color-ink)',
    border: '1px solid var(--color-line)',
    borderRadius: 'var(--radius-control)',
  },
  '.cm-tooltip-autocomplete > ul > li[aria-selected]': {
    backgroundColor: 'var(--color-accent-soft)',
    color: 'var(--color-ink)',
  },
  '.cm-completionDetail': { color: 'var(--color-muted)', fontStyle: 'normal', marginLeft: '0.75em' },
  '.cm-completionInfo': { maxWidth: '24rem', padding: '0.5rem 0.75rem' },
  '.cm-diagnostic-error': { borderLeftColor: 'var(--color-danger)' },
  '.cm-diagnostic-warning': { borderLeftColor: 'var(--color-warn)' },
  '.cm-lintRange-error': {
    backgroundImage: 'none',
    textDecoration: 'underline wavy var(--color-danger)',
    textUnderlineOffset: '3px',
  },
  '.cm-lintRange-warning': {
    backgroundImage: 'none',
    textDecoration: 'underline wavy var(--color-warn)',
    textUnderlineOffset: '3px',
  },
  '.cm-panels': { backgroundColor: 'var(--color-surface)', color: 'var(--color-ink)' },
  '.cm-searchMatch': { backgroundColor: 'var(--color-warn-soft)' },
  '.cm-foldPlaceholder': { backgroundColor: 'var(--color-sunken)', border: 'none', color: 'var(--color-muted)' },
});

/** YAML colors: keys in the accent, strings, numbers and booleans in their own calm tones. */
export const yamlHighlight = HighlightStyle.define([
  { tag: [tags.propertyName, tags.definition(tags.propertyName)], color: 'var(--color-accent)' },
  { tag: [tags.string, tags.special(tags.string)], color: 'var(--color-ok)' },
  { tag: [tags.number, tags.bool, tags.null, tags.atom], color: 'var(--color-warn)' },
  { tag: tags.comment, color: 'var(--color-muted)', fontStyle: 'italic' },
  { tag: [tags.meta, tags.punctuation, tags.separator], color: 'var(--color-muted)' },
]);
