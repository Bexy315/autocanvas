import { clsx } from 'clsx';
import { Check, CircleCheck, TriangleAlert } from 'lucide-react';
import { useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { Link, Navigate, useSearchParams } from 'react-router';
import { ApiError, describeError } from '../../api/problem';
import {
  useCompleteSetup,
  useOnlineRunner,
  useSetupStatus,
  type SetupCompleted,
  type SetupDetails,
  type SettingValue,
} from '../../api/setup';
import { AuthLayout } from '../../layout/AuthLayout';
import { guessRunnerAddress } from '../../lib/runnerAddress';
import { Alert } from '../../ui/Alert';
import { Button } from '../../ui/Button';
import { CopyValue } from '../../ui/SecretOnce';
import { Loading, Spinner } from '../../ui/Spinner';
import { TextField } from '../../ui/TextField';
import { passwordProblem, setupSteps, stepOfField, type SetupStep } from './setupModel';

interface Admin {
  userName: string;
  displayName: string;
  email: string;
  password: string;
  repeated: string;
}

const noAdmin: Admin = { userName: '', displayName: '', email: '', password: '', repeated: '' };

/** An address without the slash the server adds, as people write it. */
function shown(value: SettingValue, fallback: string): string {
  return value.value?.replace(/\/$/, '') ?? fallback;
}

/**
 * The setup of a fresh installation (ADR-0020): the token from the server's log, the first admin,
 * the addresses and the options, then the master key to back up and the bundled runner to wait
 * for. The token may come in the address or be typed in, for a server without a browser of its own.
 */
export function SetupPage() {
  const [params, setParams] = useSearchParams();
  const [checkedToken, setCheckedToken] = useState<string | null>(() => params.get('token'));
  const [typedToken, setTypedToken] = useState(() => params.get('token') ?? '');
  const open = useSetupStatus(null);
  const verified = useSetupStatus(checkedToken);
  const [step, setStep] = useState<Exclude<SetupStep, 'token'>>('admin');
  const [admin, setAdmin] = useState(noAdmin);
  const [addresses, setAddresses] = useState<{ publicAddress: string; runnerAddress: string } | null>(null);
  const [bundledRunner, setBundledRunner] = useState<boolean | null>(null);
  const [example, setExample] = useState(true);
  const complete = useCompleteSetup();
  const [completed, setCompleted] = useState<SetupCompleted | null>(null);

  // The token is taken from the address once and dropped from it, so it stays out of the history.
  useEffect(() => {
    if (params.has('token')) {
      setParams({}, { replace: true });
    }
  }, [params, setParams]);

  if (completed) {
    return <AfterSetup completed={completed} />;
  }
  if (open.isPending) {
    return <Loading />;
  }
  if (open.data && !open.data.open) {
    // Set up already: the app takes over, with the sign-in if needed.
    return <Navigate to="/" replace />;
  }

  const details = verified.data?.details ?? null;
  const wrongToken = checkedToken !== null && verified.error instanceof ApiError && verified.error.status === 401;
  const current: SetupStep = details ? step : 'token';
  const chosenAddresses = details
    ? (addresses ?? {
        publicAddress: shown(details.publicAddress, globalThis.location.origin),
        runnerAddress: shown(details.runnerAddress, guessRunnerAddress()),
      })
    : null;

  const submit = (setup: SetupDetails) => {
    complete.mutate(
      {
        token: checkedToken ?? '',
        body: {
          admin: {
            userName: admin.userName.trim(),
            password: admin.password,
            displayName: admin.displayName.trim() || null,
            email: admin.email.trim() || null,
          },
          publicAddress:
            setup.publicAddress.source === 'Configuration' ? null : (chosenAddresses?.publicAddress ?? null),
          runnerAddress:
            setup.runnerAddress.source === 'Configuration' ? null : (chosenAddresses?.runnerAddress ?? null),
          bundledRunner: setup.bundledRunner && (bundledRunner ?? true),
          example,
        },
      },
      {
        onSuccess: setCompleted,
        onError: (error) => {
          if (error instanceof ApiError && error.status === 401) {
            setCheckedToken(null);
            return;
          }
          const back = error instanceof ApiError ? stepOfField(error.problem.field) : null;
          if (back && back !== 'token') {
            setStep(back);
          }
        },
      },
    );
  };

  return (
    <AuthLayout title="Set up AutoCanvas" wide>
      <Progress current={current} />
      {current === 'token' && (
        <TokenStep
          token={typedToken}
          onToken={setTypedToken}
          checking={verified.isFetching}
          problem={
            wrongToken
              ? "This is not this server's setup token. Copy it from the server's log again; it changes when the server restarts."
              : verified.isError && checkedToken !== null
                ? describeError(verified.error)
                : complete.error instanceof ApiError && complete.error.status === 401
                  ? 'The server restarted, so its setup token changed. Enter the new one from its log.'
                  : null
          }
          onContinue={() => setCheckedToken(typedToken.trim())}
        />
      )}
      {current === 'admin' && (
        <AdminStep
          admin={admin}
          onAdmin={setAdmin}
          error={complete.error}
          onBack={() => setCheckedToken(null)}
          onContinue={() => setStep('addresses')}
        />
      )}
      {current === 'addresses' && details && chosenAddresses && (
        <AddressesStep
          details={details}
          addresses={chosenAddresses}
          onAddresses={setAddresses}
          error={complete.error}
          onBack={() => setStep('admin')}
          onContinue={() => setStep('options')}
        />
      )}
      {current === 'options' && details && (
        <OptionsStep
          details={details}
          bundledRunner={details.bundledRunner && (bundledRunner ?? true)}
          onBundledRunner={setBundledRunner}
          example={example}
          onExample={setExample}
          busy={complete.isPending}
          error={complete.error}
          onBack={() => setStep('addresses')}
          onSubmit={() => submit(details)}
        />
      )}
    </AuthLayout>
  );
}

/** Where in the setup the user is: the steps as a list, the current one marked. */
function Progress({ current }: { current: SetupStep }) {
  const index = setupSteps.findIndex((step) => step.id === current);
  return (
    <ol aria-label="Setup steps" className="mb-5 flex items-center gap-2 text-xs">
      {setupSteps.map((step, position) => (
        <li
          key={step.id}
          aria-current={position === index ? 'step' : undefined}
          className={clsx(
            'flex items-center gap-1.5',
            position === index ? 'font-medium text-ink' : position < index ? 'text-ink' : 'text-muted',
          )}
        >
          <span
            aria-hidden="true"
            className={clsx(
              'flex size-5 items-center justify-center rounded-full border text-[0.7rem]',
              position < index && 'border-accent bg-accent text-accent-ink',
              position === index && 'border-accent text-accent',
              position > index && 'border-line-strong',
            )}
          >
            {position < index ? <Check size={11} /> : position + 1}
          </span>
          {step.label}
          {position < setupSteps.length - 1 && <span aria-hidden="true" className="ml-0.5 h-px w-4 bg-line-strong" />}
        </li>
      ))}
    </ol>
  );
}

function StepForm({
  onSubmit,
  onBack,
  submitLabel = 'Continue',
  busy = false,
  children,
}: {
  onSubmit: () => void;
  onBack?: () => void;
  submitLabel?: string;
  busy?: boolean;
  children: ReactNode;
}) {
  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit();
  };
  return (
    <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
      {children}
      <div className="flex justify-between gap-2">
        {onBack ? (
          <Button variant="ghost" onClick={onBack} disabled={busy}>
            Back
          </Button>
        ) : (
          <span />
        )}
        <Button type="submit" variant="primary" busy={busy}>
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}

function TokenStep({
  token,
  onToken,
  checking,
  problem,
  onContinue,
}: {
  token: string;
  onToken: (token: string) => void;
  checking: boolean;
  problem: string | null;
  onContinue: () => void;
}) {
  const [empty, setEmpty] = useState(false);
  return (
    <StepForm
      busy={checking}
      onSubmit={() => {
        setEmpty(token.trim() === '');
        if (token.trim() !== '') {
          onContinue();
        }
      }}
    >
      <p className="text-sm text-muted">
        Only someone who can read the server&apos;s log can set it up. The log names the setup address with its token.
      </p>
      <TextField
        label="Setup token"
        value={token}
        autoComplete="off"
        spellCheck={false}
        hint="In the server's log, after token= in the setup address; with the compose package: docker compose logs server."
        errors={empty ? ['Enter the setup token.'] : problem ? [problem] : []}
        onChange={(event) => onToken(event.target.value)}
      />
    </StepForm>
  );
}

function AdminStep({
  admin,
  onAdmin,
  error,
  onBack,
  onContinue,
}: {
  admin: Admin;
  onAdmin: (admin: Admin) => void;
  error: Error | null;
  onBack: () => void;
  onContinue: () => void;
}) {
  const [checked, setChecked] = useState(false);
  const fieldErrors = (field: string) => (error instanceof ApiError ? error.fieldErrors(`admin.${field}`) : []);
  const nameMissing = checked && admin.userName.trim() === '';
  const passwordWrong = checked ? passwordProblem(admin.password, admin.repeated, admin.userName) : null;
  const set = (field: keyof Admin) => (event: { target: { value: string } }) =>
    onAdmin({ ...admin, [field]: event.target.value });
  return (
    <StepForm
      onBack={onBack}
      onSubmit={() => {
        setChecked(true);
        if (admin.userName.trim() !== '' && passwordProblem(admin.password, admin.repeated, admin.userName) === null) {
          onContinue();
        }
      }}
    >
      <p className="text-sm text-muted">The first admin. More people can be added once AutoCanvas is set up.</p>
      <TextField
        label="User name"
        value={admin.userName}
        autoComplete="username"
        errors={nameMissing ? ['Enter a user name.'] : fieldErrors('userName')}
        onChange={set('userName')}
      />
      <TextField
        label="Display name"
        value={admin.displayName}
        hint="Optional; shown to other people instead of the user name."
        errors={fieldErrors('displayName')}
        onChange={set('displayName')}
      />
      <TextField
        label="E-mail"
        type="email"
        value={admin.email}
        hint="Optional."
        errors={fieldErrors('email')}
        onChange={set('email')}
      />
      <TextField
        label="Password"
        type="password"
        value={admin.password}
        autoComplete="new-password"
        hint="At least 12 characters."
        errors={
          passwordWrong && passwordWrong !== 'The two passwords differ.' ? [passwordWrong] : fieldErrors('password')
        }
        onChange={set('password')}
      />
      <TextField
        label="Repeat the password"
        type="password"
        value={admin.repeated}
        autoComplete="new-password"
        errors={passwordWrong === 'The two passwords differ.' ? [passwordWrong] : []}
        onChange={set('repeated')}
      />
    </StepForm>
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
  setting: SettingValue;
  value: string;
  errors: string[];
  onValue: (value: string) => void;
}) {
  const locked = setting.source === 'Configuration';
  return (
    <TextField
      label={label}
      value={locked ? shown(setting, '') : value}
      readOnly={locked}
      hint={locked ? "Set by the server's configuration; change it there." : hint}
      errors={errors}
      onChange={(event) => onValue(event.target.value)}
    />
  );
}

function AddressesStep({
  details,
  addresses,
  onAddresses,
  error,
  onBack,
  onContinue,
}: {
  details: SetupDetails;
  addresses: { publicAddress: string; runnerAddress: string };
  onAddresses: (addresses: { publicAddress: string; runnerAddress: string }) => void;
  error: Error | null;
  onBack: () => void;
  onContinue: () => void;
}) {
  const fieldErrors = (field: string) => (error instanceof ApiError ? error.fieldErrors(field) : []);
  return (
    <StepForm onBack={onBack} onSubmit={onContinue}>
      <p className="text-sm text-muted">
        Where AutoCanvas is reached. They appear in webhook addresses and in the commands that register runners, and can
        be changed later under Administration.
      </p>
      <AddressField
        label="Public address"
        hint="As people and other systems reach this server, e.g. https://autocanvas.example.org behind a proxy."
        setting={details.publicAddress}
        value={addresses.publicAddress}
        errors={fieldErrors('publicAddress')}
        onValue={(publicAddress) => onAddresses({ ...addresses, publicAddress })}
      />
      <AddressField
        label="Runner address"
        hint="As runners reach the server's runner port (5081 unless configured otherwise)."
        setting={details.runnerAddress}
        value={addresses.runnerAddress}
        errors={fieldErrors('runnerAddress')}
        onValue={(runnerAddress) => onAddresses({ ...addresses, runnerAddress })}
      />
    </StepForm>
  );
}

function Choice({
  label,
  description,
  checked,
  onChecked,
}: {
  label: string;
  description: string;
  checked: boolean;
  onChecked: (checked: boolean) => void;
}) {
  const id = useId();
  return (
    <div className="flex flex-col gap-0.5 rounded-control border border-line p-3 text-sm">
      <label htmlFor={id} className="flex items-center gap-2.5 font-medium">
        <input
          id={id}
          type="checkbox"
          checked={checked}
          aria-describedby={`${id}-description`}
          onChange={(event) => onChecked(event.target.checked)}
          className="size-4 shrink-0 accent-(--color-accent)"
        />
        {label}
      </label>
      <p id={`${id}-description`} className="pl-6.5 text-muted">
        {description}
      </p>
    </div>
  );
}

function OptionsStep({
  details,
  bundledRunner,
  onBundledRunner,
  example,
  onExample,
  busy,
  error,
  onBack,
  onSubmit,
}: {
  details: SetupDetails;
  bundledRunner: boolean;
  onBundledRunner: (value: boolean) => void;
  example: boolean;
  onExample: (value: boolean) => void;
  busy: boolean;
  error: Error | null;
  onBack: () => void;
  onSubmit: () => void;
}) {
  const general = error && !(error instanceof ApiError && error.problem.field) ? describeError(error) : null;
  return (
    <StepForm onBack={onBack} onSubmit={onSubmit} submitLabel="Set up AutoCanvas" busy={busy}>
      {details.bundledRunner && (
        <Choice
          label="Register the bundled runner"
          description="The runner that came with the package registers itself and runs jobs right away (labels linux and docker)."
          checked={bundledRunner}
          onChecked={onBundledRunner}
        />
      )}
      <Choice
        label="Create an example automation"
        description="A folder Getting started with an automation that greets and writes a small test and coverage report."
        checked={example}
        onChecked={onExample}
      />
      {general && <Alert tone="danger">{general}</Alert>}
    </StepForm>
  );
}

/** After the setup: the master key to back up, the bundled runner to wait for, then into the app. */
function AfterSetup({ completed }: { completed: SetupCompleted }) {
  const [keyStored, setKeyStored] = useState(completed.masterKey === null);
  const [runnerSkipped, setRunnerSkipped] = useState(false);
  const waiting = keyStored && completed.runnerTokenWritten && !runnerSkipped;
  const runner = useOnlineRunner(waiting);

  if (!keyStored && completed.masterKey) {
    return <MasterKeyStep backup={completed.masterKey} onStored={() => setKeyStored(true)} />;
  }
  if (waiting && !runner.data) {
    return (
      <AuthLayout title="Waiting for the bundled runner" wide>
        <div className="flex flex-col gap-4">
          <p className="flex items-center gap-2 text-sm">
            <Spinner />
            The runner that came with the package is registering itself. This takes a few seconds once it is running.
          </p>
          {runner.isError && <Alert tone="danger">{describeError(runner.error)}</Alert>}
          <div className="flex justify-end">
            <Button onClick={() => setRunnerSkipped(true)}>Continue without waiting</Button>
          </div>
        </div>
      </AuthLayout>
    );
  }
  return (
    <AuthLayout title="AutoCanvas is set up" wide>
      <div className="flex flex-col gap-4">
        <p className="flex items-center gap-2 text-sm">
          <CircleCheck aria-hidden="true" size={18} className="text-ok" />
          You are signed in as {completed.me.user.displayName}.
        </p>
        {runner.data && (
          <p className="flex items-center gap-2 text-sm">
            <CircleCheck aria-hidden="true" size={18} className="text-ok" />
            The runner {runner.data.name} is online.
          </p>
        )}
        <div className="flex justify-end gap-2">
          {completed.exampleAutomationId ? (
            <>
              <Link to="/" className="inline-flex h-8 items-center rounded-control px-3 text-sm hover:bg-sunken">
                Go to AutoCanvas
              </Link>
              <Link
                to={`/automations/${completed.exampleAutomationId}`}
                className="inline-flex h-8 items-center rounded-control bg-accent px-3 text-sm font-medium text-accent-ink hover:bg-accent-hover"
              >
                Open the example
              </Link>
            </>
          ) : (
            <Link
              to="/"
              className="inline-flex h-8 items-center rounded-control bg-accent px-3 text-sm font-medium text-accent-ink hover:bg-accent-hover"
            >
              Go to AutoCanvas
            </Link>
          )}
        </div>
      </div>
    </AuthLayout>
  );
}

function MasterKeyStep({ backup, onStored }: { backup: { key: string; file: string }; onStored: () => void }) {
  const [stored, setStored] = useState(false);
  return (
    <AuthLayout title="Back up the master key" wide>
      <div className="flex flex-col gap-4">
        <p className="flex gap-2 rounded-control bg-warn-soft px-3 py-2 text-sm">
          <TriangleAlert aria-hidden="true" size={16} className="mt-0.5 shrink-0 text-warn" />
          AutoCanvas encrypts secrets with this key. Without it, the secrets in a backup cannot be read again; with it
          and the database, anyone can. Store it somewhere safe, apart from the database.
        </p>
        <CopyValue label="Master key" value={backup.key} />
        <CopyValue label="Kept by the server in" value={backup.file} />
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            checked={stored}
            onChange={(event) => setStored(event.target.checked)}
            className="size-4 accent-(--color-accent)"
          />
          I have stored the master key
        </label>
        <div className="flex justify-end">
          <Button variant="primary" disabled={!stored} onClick={onStored}>
            Continue
          </Button>
        </div>
      </div>
    </AuthLayout>
  );
}
