/**
 * Leaves the page by posting a form to another site, as GitHub's manifest flow wants it: a plain
 * form submission, not a fetch, so that the browser follows to GitHub with the user's own session
 * there.
 */
export function postForm(action: string, fields: Record<string, string>): void {
  const form = document.createElement('form');
  form.method = 'post';
  form.action = action;
  form.hidden = true;
  for (const [name, value] of Object.entries(fields)) {
    const input = document.createElement('input');
    input.type = 'hidden';
    input.name = name;
    input.value = value;
    form.append(input);
  }
  document.body.append(form);
  form.submit();
}
