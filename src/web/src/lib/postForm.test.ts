import { describe, expect, it, vi } from 'vitest';
import { postForm } from './postForm';

describe('posting a form to another site', () => {
  it('submits a hidden form with the fields', () => {
    const submit = vi.spyOn(HTMLFormElement.prototype, 'submit').mockImplementation(() => undefined);

    postForm('https://github.com/settings/apps/new?state=s', { manifest: '{"a":1}' });

    const form = document.querySelector('form');
    if (!form) {
      throw new Error('No form was made.');
    }
    expect(form.method).toBe('post');
    expect(form.action).toBe('https://github.com/settings/apps/new?state=s');
    expect(form.hidden).toBe(true);
    expect(new FormData(form).get('manifest')).toBe('{"a":1}');
    expect(submit).toHaveBeenCalledOnce();
    form.remove();
    submit.mockRestore();
  });
});
