import { ScrollRegion } from './ScrollRegion';

/** A file to read, with line numbers; the YAML editor replaces it where editing is possible (S3 step 4). */
export function YamlView({ text, label }: { text: string; label: string }) {
  const lines = text.replace(/\n$/, '').split('\n');
  return (
    <ScrollRegion label={label} className="bg-sunken/50 py-2">
      <table className="border-collapse">
        <tbody>
          {lines.map((line, index) => (
            // Lines have no identity beyond their position.
            // oxlint-disable-next-line react/no-array-index-key
            <tr key={index}>
              <td className="pr-4 pl-4 text-right text-muted select-none">{index + 1}</td>
              <td className="pr-4 whitespace-pre text-ink">{line}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </ScrollRegion>
  );
}
