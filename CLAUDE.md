@AGENTS.md

## Claude Code specifics

- Skills in `.claude/skills/` are loaded automatically; invoke them by name for the tasks listed in
  the table above.
- Use plan mode for anything larger than a single focused change, and put lasting plans in
  `docs/plans/` rather than only in the conversation.
- The maintainer runs other agents in this repository in parallel. Re-read files and `git status`
  before editing instead of relying on what you saw earlier in the session.
