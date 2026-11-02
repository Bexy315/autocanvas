import type { DiffHunk } from '../lib/diff';
import { ScrollRegion } from './ScrollRegion';

const marks = { added: '+', removed: '−', same: ' ' } as const;
const lineStyles = { added: 'bg-ok-soft', removed: 'bg-danger-soft', same: '' } as const;

/** Changed lines with a little context; gaps say how many unchanged lines they stand for. */
export function DiffView({ hunks, label }: { hunks: readonly DiffHunk[]; label: string }) {
  return (
    <ScrollRegion label={label} className="rounded-control border border-line">
      <table className="w-full border-collapse">
        <tbody>
          {hunks.flatMap((hunk) => [
            ...(hunk.skipped > 0
              ? [
                  <tr
                    key={`gap-${hunk.lines[0]?.before ?? 'end'}-${hunk.lines[0]?.after ?? 'end'}`}
                    className="bg-sunken text-muted"
                  >
                    <td colSpan={4} className="px-3 py-1 font-sans">
                      {hunk.skipped} unchanged {hunk.skipped === 1 ? 'line' : 'lines'}
                    </td>
                  </tr>,
                ]
              : []),
            ...hunk.lines.map((line) => (
              <tr key={`${line.before ?? '-'}-${line.after ?? '-'}`} className={lineStyles[line.kind]}>
                <td className="w-10 pr-2 pl-3 text-right text-muted select-none">{line.before ?? ''}</td>
                <td className="w-10 pr-2 text-right text-muted select-none">{line.after ?? ''}</td>
                <td className="w-4 text-center select-none" aria-label={line.kind === 'same' ? undefined : line.kind}>
                  {marks[line.kind]}
                </td>
                <td className="pr-3 whitespace-pre">{line.text}</td>
              </tr>
            )),
          ])}
        </tbody>
      </table>
    </ScrollRegion>
  );
}
