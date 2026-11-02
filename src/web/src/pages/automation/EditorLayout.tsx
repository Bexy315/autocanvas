import { Outlet, useParams } from 'react-router';
import { ErrorAlert } from '../../ui/Alert';
import { Loading } from '../../ui/Spinner';
import { useAutomationPage } from './useAutomationPage';

/**
 * The automation's editor fills the whole window, without the app's sidebar, header and folder
 * tree: the canvas is easier to work on with all the room there is. Its own bar leads back.
 */
export function EditorLayout() {
  const { id = '' } = useParams();
  const page = useAutomationPage(id);

  if (page.failed) {
    return (
      <div className="mx-auto max-w-5xl p-6">
        <ErrorAlert error={page.error} title="The automation could not be loaded" />
      </div>
    );
  }
  if (!page.context) {
    return <Loading label="Loading the automation…" />;
  }
  return (
    <div className="h-dvh bg-canvas text-ink">
      <Outlet key={id} context={page.context} />
    </div>
  );
}
