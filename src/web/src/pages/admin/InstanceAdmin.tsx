import { useState, type FormEvent } from 'react';
import { useInstanceSettings, useUpdateInstanceSettings, type InstanceSettings } from '../../api/instance';
import { ApiError } from '../../api/problem';
import { Alert, ErrorAlert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { Loading } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';

type Setting = InstanceSettings['publicAddress'];

/** An address as people write it, without the slash the server adds. */
function text(setting: Setting): string {
  return setting.value?.replace(/\/$/, '') ?? '';
}

/** What to send for an address: nothing for one the configuration sets, else the value or nothing. */
function sent(setting: Setting, value: string): string | null {
  return setting.source === 'Configuration' ? null : value.trim() || null;
}

/** The installation's addresses (ADR-0020), locked where the server's configuration sets them. */
export function InstanceAdmin() {
  const settings = useInstanceSettings();
  if (settings.isPending) {
    return <Loading />;
  }
  if (settings.isError) {
    return <ErrorAlert error={settings.error} title="The instance settings could not be loaded" />;
  }
  return <InstanceForm settings={settings.data} />;
}

function InstanceForm({ settings }: { settings: InstanceSettings }) {
  const update = useUpdateInstanceSettings();
  const [publicAddress, setPublicAddress] = useState(() => text(settings.publicAddress));
  const [runnerAddress, setRunnerAddress] = useState(() => text(settings.runnerAddress));
  const fieldErrors = (field: string) => (update.error instanceof ApiError ? update.error.fieldErrors(field) : []);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    update.mutate({
      publicAddress: sent(settings.publicAddress, publicAddress),
      runnerAddress: sent(settings.runnerAddress, runnerAddress),
    });
  };

  return (
    <form onSubmit={submit} className="flex max-w-xl flex-col gap-4" noValidate>
      <p className="text-sm text-muted">
        Where this installation is reached. The addresses appear in webhook addresses and in the commands that register
        runners.
      </p>
      <AddressField
        label="Public address"
        hint="As people and other systems reach the server, e.g. https://autocanvas.example.org behind a proxy. Also decides whether the sign-in cookie is sent over HTTPS only."
        setting={settings.publicAddress}
        value={publicAddress}
        errors={fieldErrors('publicAddress')}
        onValue={setPublicAddress}
      />
      <AddressField
        label="Runner address"
        hint="As runners reach the server's runner port, e.g. http://autocanvas.example.org:5081."
        setting={settings.runnerAddress}
        value={runnerAddress}
        errors={fieldErrors('runnerAddress')}
        onValue={setRunnerAddress}
      />
      {update.isError && !(update.error instanceof ApiError && update.error.problem.field) && (
        <ErrorAlert error={update.error} title="The addresses were not saved" />
      )}
      {update.isSuccess && !update.isPending && <Alert tone="ok">Saved.</Alert>}
      <div>
        <Button type="submit" variant="primary" busy={update.isPending}>
          Save
        </Button>
      </div>
    </form>
  );
}

function AddressField({
  label,
  hint,
  setting,
  value,
  errors,
  onValue,
}: {
  label: string;
  hint: string;
  setting: Setting;
  value: string;
  errors: string[];
  onValue: (value: string) => void;
}) {
  const locked = setting.source === 'Configuration';
  return (
    <TextField
      label={label}
      value={locked ? text(setting) : value}
      readOnly={locked}
      placeholder={locked ? undefined : 'Not set'}
      hint={
        locked ? "Set by the server's configuration (Server:PublicUrl or Runners:PublicUrl); change it there." : hint
      }
      errors={errors}
      onChange={(event) => onValue(event.target.value)}
    />
  );
}
