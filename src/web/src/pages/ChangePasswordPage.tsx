import { Navigate, useNavigate, useSearchParams } from 'react-router';
import { useMe } from '../api/session';
import { safeReturnTo } from '../app/returnTo';
import { AuthLayout } from '../layout/AuthLayout';
import { PasswordForm } from './PasswordForm';

/**
 * The password change that comes before everything else when someone else set the password
 * (the first admin, a reset). Without that need, the account page is the place to change it.
 */
export function ChangePasswordPage() {
  const me = useMe();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const returnTo = safeReturnTo(params.get('returnTo'));

  if (me.data && !me.data.user.mustChangePassword) {
    return <Navigate to={returnTo} replace />;
  }

  return (
    <AuthLayout
      title="Choose your own password"
      intro="The password you signed in with was set for you. Replace it before you continue; your other sessions end."
    >
      <PasswordForm
        submitLabel="Change password and continue"
        onChanged={() => void navigate(returnTo, { replace: true })}
      />
    </AuthLayout>
  );
}
