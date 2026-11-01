import { Link } from 'react-router';

export function NotFoundPage() {
  return (
    <div className="mx-auto flex max-w-md flex-col items-start gap-3 p-8">
      <h1 className="text-lg font-semibold">There is no page here</h1>
      <p className="text-muted">The address may be mistyped, or what it pointed to was moved or deleted.</p>
      <Link to="/" className="font-medium text-accent hover:underline">
        Go to automations
      </Link>
    </div>
  );
}
