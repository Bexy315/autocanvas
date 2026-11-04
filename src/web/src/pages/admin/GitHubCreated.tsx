import { useEffect, useRef } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { useCompleteGitHubApp } from '../../api/forges';
import { Alert, ErrorAlert } from '../../ui/Alert';
import { Loading } from '../../ui/Spinner';

/**
 * Where GitHub sends the browser back after creating the app: the code it hands over is turned
 * into the app once, then the Forges tab shows it.
 */
export function GitHubCreated() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const complete = useCompleteGitHubApp();
  const sent = useRef(false);
  const code = params.get('code');
  const state = params.get('state');

  useEffect(() => {
    // Once only, also when React runs effects twice: a code can be turned into the app once.
    if (sent.current || !code || !state) {
      return;
    }
    sent.current = true;
    complete.mutate({ code, state }, { onSuccess: () => void navigate('/admin/forges', { replace: true }) });
  }, [code, state, complete, navigate]);

  if (!code || !state) {
    return (
      <Alert tone="warn" title="Nothing to finish here">
        GitHub sends you here after creating the app. <Link to="/admin/forges">Back to Forges</Link>
      </Alert>
    );
  }
  if (complete.isError) {
    return (
      <div className="flex flex-col gap-3">
        <ErrorAlert error={complete.error} title="The GitHub App could not be finished" />
        <Link to="/admin/forges" className="text-sm text-accent hover:underline">
          Back to Forges, to create it again
        </Link>
      </div>
    );
  }
  return <Loading label="Finishing the GitHub App…" />;
}
