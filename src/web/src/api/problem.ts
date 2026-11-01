/** Problem details as the API returns them (RFC 9457), with field errors for validation problems. */
export interface Problem {
  type?: string | null;
  title?: string | null;
  status?: number | string | null;
  detail?: string | null;
  errors?: Record<string, string[]>;
}

/** A request the API refused or could not answer; `problem` holds what the server said. */
export class ApiError extends Error {
  readonly status: number;
  readonly problem: Problem;

  constructor(status: number, problem: Problem) {
    super(problem.detail ?? problem.title ?? `The server answered ${status}.`);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
  }

  /** The messages for one field of a validation problem, e.g. `newPassword`. */
  fieldErrors(field: string): string[] {
    return this.problem.errors?.[field] ?? [];
  }
}

/** Reads a body as problem details; anything else becomes a problem with just the status. */
export function toProblem(body: unknown, status: number): Problem {
  if (typeof body === 'object' && body !== null) {
    return body as Problem;
  }
  return { status, title: typeof body === 'string' && body.length > 0 ? body : null };
}

/** A sentence for any error, for places that show one message. */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) {
    const fields = Object.values(error.problem.errors ?? {}).flat();
    return error.problem.detail ?? fields[0] ?? error.problem.title ?? error.message;
  }
  if (error instanceof TypeError) {
    return 'The server could not be reached. Check your connection and try again.';
  }
  return error instanceof Error ? error.message : 'Something went wrong.';
}
