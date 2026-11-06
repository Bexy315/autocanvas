import { useState } from 'react';
import type { NodeTypeView } from '../operations';
import { isPlainMap, isTextList, type PanelFile } from '../panelModel';
import type { Path } from '../yamlEdit';
import { Choice, KeyValueList, NumberInput, TagList, TextInput, YamlOnly } from './fields';
import { usePanel, useProblems } from './panelContext';

type Input = NodeTypeView['inputs'][number];

const isExpression = (value: unknown) => typeof value === 'string' && value.includes('${{');

/**
 * The inputs of a node type's step, under `with`, each in the field its kind asks for. Any input may
 * be an expression instead; a field that holds one shows it as text.
 */
export function NodeInputs({
  nodeType,
  file,
  path,
  onSet,
}: {
  nodeType: NodeTypeView;
  file: PanelFile;
  /** The step's path; inputs are below `with`. */
  path: Path;
  onSet: (path: Path, value: unknown, summary: string) => void;
}) {
  if (nodeType.inputs.length === 0) {
    return <p className="text-sm text-muted">{nodeType.name} takes no inputs.</p>;
  }
  return (
    <>
      {nodeType.inputs.map((input) => (
        <InputField key={input.name} input={input} file={file} path={[...path, 'with', input.name]} onSet={onSet} />
      ))}
    </>
  );
}

function InputField({
  input,
  file,
  path,
  onSet,
}: {
  input: Input;
  file: PanelFile;
  path: Path;
  onSet: (path: Path, value: unknown, summary: string) => void;
}) {
  const { readOnly } = usePanel();
  const value = file.get(path);
  const problems = useProblems(path);
  const [asExpression, setAsExpression] = useState(false);
  const label = input.required ? `${input.name} (required)` : input.name;
  const set = (next: unknown) => onSet(path, next, `Changed ${input.name}`);
  const frame = { label, hint: input.description, problems };

  if (file.shared(path)) {
    return <YamlOnly label={label} source={file.source(path)} />;
  }
  if (input.kind !== 'Text' && (asExpression || isExpression(value))) {
    return <TextInput {...frame} value={typeof value === 'string' ? value : ''} onValue={set} mono />;
  }
  const expressionSwitch = !readOnly && input.kind !== 'Text' && (
    <button type="button" onClick={() => setAsExpression(true)} className="w-fit text-xs text-accent hover:underline">
      Use an expression for {input.name}
    </button>
  );

  switch (input.kind) {
    case 'Text':
      return input.choices.length > 0 && !isExpression(value) ? (
        <Choice
          {...frame}
          value={typeof value === 'string' ? value : ''}
          options={input.choices.map((choice) => ({ value: choice, label: choice }))}
          empty="Not set"
          onValue={set}
        />
      ) : (
        <TextInput {...frame} value={value === undefined ? '' : String(value)} onValue={set} expressions />
      );
    case 'WholeNumber':
      return (
        <>
          <NumberInput {...frame} value={value} onValue={set} />
          {expressionSwitch}
        </>
      );
    case 'Boolean':
      return (
        <>
          <Choice
            {...frame}
            value={value === undefined ? '' : String(value)}
            options={[
              { value: 'true', label: 'Yes' },
              { value: 'false', label: 'No' },
            ]}
            empty="Not set"
            onValue={(choice) => set(choice === '' ? undefined : choice === 'true')}
          />
          {expressionSwitch}
        </>
      );
    case 'TextList':
      return isTextList(value) || value === undefined ? (
        <>
          <TagList {...frame} values={value ?? []} onValues={set} />
          {expressionSwitch}
        </>
      ) : (
        <YamlOnly label={label} source={file.source(path)} />
      );
    case 'TextMap':
      return isPlainMap(value) || value === undefined ? (
        <KeyValueList label={label} hint={input.description} entries={value ?? {}} onEntries={set} />
      ) : (
        <YamlOnly label={label} source={file.source(path)} />
      );
    default:
      return <YamlOnly label={label} source={file.source(path)} />;
  }
}
