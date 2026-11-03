import { useState, type FormEvent } from 'react';
import { Navigate, useNavigate, useSearchParams } from 'react-router';
import { ApiError, describeError, isSetupRequired } from '../api/problem';
import { useMe, useSignIn } from '../api/session';
import { returnQuery, safeReturnTo } from '../app/returnTo';
import { AuthLayout } from '../layout/AuthLayout';
import { Alert } from '../ui/Alert';
import { Button } from '../ui/Button';
import { Loading } from '../ui/Spinner';
import { TextField } from '../ui/TextField';

function signInError(error: unknown): string {
  if (error instanceof ApiError && error.status === 401) {
    return 'The user name or password is wrong, or the account is locked for a while after too many attempts.';
  }
  if (error instanceof ApiError && error.status === 429) {
    return 'Too many attempts from this address. Wait a minute, then try again.';
  }
  return describeError(error);
}

export function SignInPage() {
  const [params] = useSearchParams();
  const returnTo = safeReturnTo(params.get('returnTo'));
  const expired = params.get('expired') === '1';
  const me = useMe();
  const signIn = useSignIn();
  const navigate = useNavigate();
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');

  if (me.isPending) {
    return <Loading />;
  }
  if (isSetupRequired(me.error)) {
    return <Navigate to="/setup" replace />;
  }
  if (me.data && !signIn.isPending) {
    return <Navigate to={returnTo} replace />;
  }

  const submit = (event: FormEvent) => {
    event.preventDefault();
    signIn.mutate(
      { userName, password },
      {
        onSuccess: (signedIn) => {
          const next = signedIn.me.user.mustChangePassword ? `/change-password${returnQuery(returnTo)}` : returnTo;
          void navigate(next, { replace: true });
        },
        onError: () => setPassword(''),
      },
    );
  };

  return (
    <AuthLayout title="Sign in">
      <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
        {expired && !signIn.isError && (
          <Alert tone="info" title="Your session has ended">
            Sign in again to continue where you left off.
          </Alert>
        )}
        {signIn.isError && <Alert tone="danger">{signInError(signIn.error)}</Alert>}
        <TextField
          label="User name"
          name="userName"
          autoComplete="username"
          autoCapitalize="none"
          spellCheck={false}
          required
          value={userName}
          onChange={(event) => setUserName(event.target.value)}
        />
        <TextField
          label="Password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
        <Button
          type="submit"
          variant="primary"
          busy={signIn.isPending}
          disabled={userName.length === 0 || password.length === 0}
          className="mt-1 w-full"
        >
          Sign in
        </Button>
      </form>
    </AuthLayout>
  );
}
