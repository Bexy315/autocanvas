/** The steps of the setup form, in order. */
export const setupSteps = [
  { id: 'token', label: 'Token' },
  { id: 'admin', label: 'Admin' },
  { id: 'addresses', label: 'Addresses' },
  { id: 'options', label: 'Options' },
] as const;

export type SetupStep = (typeof setupSteps)[number]['id'];

/** The step whose field a problem from the server is about, to go back to it. */
export function stepOfField(field: string | null | undefined): SetupStep | null {
  if (!field) {
    return null;
  }
  if (field.startsWith('admin.')) {
    return 'admin';
  }
  if (field === 'publicAddress' || field === 'runnerAddress') {
    return 'addresses';
  }
  return field === 'bundledRunner' ? 'options' : null;
}

/** What is wrong with the new admin's password before the server is asked, or `null`. */
export function passwordProblem(password: string, repeated: string, userName: string): string | null {
  if (password.length < 12) {
    return 'Use at least 12 characters.';
  }
  if (password.trim().toLowerCase() === userName.trim().toLowerCase()) {
    return 'The password must not be the user name.';
  }
  return password === repeated ? null : 'The two passwords differ.';
}
