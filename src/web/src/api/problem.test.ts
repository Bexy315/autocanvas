import { describe, expect, it } from 'vitest';
import { ApiError, describeError, toProblem } from './problem';

describe('problems', () => {
  it.each([
    [new ApiError(409, { detail: 'Someone saved first.' }), 'Someone saved first.'],
    [new ApiError(400, { title: 'Bad', errors: { name: ['Name is required.'] } }), 'Name is required.'],
    [new ApiError(403, { title: 'Forbidden' }), 'Forbidden'],
    [new ApiError(500, {}), 'The server answered 500.'],
    [
      new ApiError(502, {}),
      'The server cannot be reached right now. It may be starting or restarting; try again in a moment.',
    ],
    [
      new ApiError(503, { detail: 'Not ready.' }),
      'The server cannot be reached right now. It may be starting or restarting; try again in a moment.',
    ],
    [
      new ApiError(504, {}),
      'The server cannot be reached right now. It may be starting or restarting; try again in a moment.',
    ],
    [
      new TypeError('Failed to fetch'),
      'The server cannot be reached right now. It may be starting or restarting; try again in a moment.',
    ],
    [
      new ApiError(503, { type: 'https://bexy315.github.io/autocanvas/technical/api#setup-required' }),
      "AutoCanvas is not set up yet. Open the setup page and enter the setup token from the server's log.",
    ],
    [new Error('Broken.'), 'Broken.'],
    ['a string', 'Something went wrong.'],
  ])('describes %s in one sentence', (error, sentence) => {
    expect(describeError(error)).toBe(sentence);
  });

  it.each([
    [{ title: 'Conflict', status: 409 }, 409, { title: 'Conflict', status: 409 }],
    ['Bad Gateway', 502, { status: 502, title: 'Bad Gateway' }],
    [undefined, 500, { status: 500, title: null }],
  ])('reads %j as a problem', (body, status, expected) => {
    expect(toProblem(body, status)).toEqual(expected);
  });
});
