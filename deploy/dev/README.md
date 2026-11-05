# A throwaway dev instance

The files here run an AutoCanvas instance for manual tests:
- it always runs the newest `main`;
- it resets itself every night, and on demand, to the state described below;
- it is the [compose package](../compose/) with PostgreSQL, from the images CI publishes.

The plan is [D1](../../docs/plans/d1.md); [ADR-0023](../../docs/adr/0023-published-images-and-dev-instance.md)
says why it is built this way. The host and the deploy follow in D1's later steps.

## The seed

`seed.sh` fills a **fresh** instance through the public API, as a person in the web UI would. On an
instance that is set up already it says so and changes nothing.

```sh
AUTOCANVAS_SEED_PASSWORD='…' deploy/dev/seed.sh --url http://localhost:5080 --token <setup token> \
  --public-address https://dev.example.org
```

- The setup token is in the server's log (`docker compose logs server`).
- `--public-address` is optional.
- All users get the seed password (at least 12 characters). It needs bash, `curl` and `jq`.

| What | Seeded |
|---|---|
| Users | `admin` (administrator), `viewer` (Vera Viewer), `operator` (Otto Operator), `editor` (Edith Editor). Each has changed their initial password already, so they sign in with the seed password. |
| Group | `testers`: viewer, operator and editor |
| Folders | **Getting started** (the setup's example) · **Team**, with **Team / Builds** and **Team / Restricted** |
| Rights | On Team: `testers` Viewer, `operator` Operator, `editor` Editor. Restricted takes no rights from above; only `editor` has Editor there. |
| Configuration | On Team: the variable `GREETING` and the secret `DEPLOY_TOKEN` (a new random value on every reset) |
| Automations | `hello` (the setup's example) · `build-and-test` in Builds: two jobs, a JUnit report with 5 tests (1 skipped) and coverage, and a schedule every 6 hours · `uses-secret`: prints the variable and uses the secret, which the log masks · `fails-on-purpose`: always fails · `deploy` in Restricted |
| Runs | A first run of each automation but `deploy`. `fails-on-purpose`'s fails; the others succeed. The seed fails if any ends otherwise. |
| Runner | The compose package's bundled runner (`linux`, `docker`) |

So, for example:
- `viewer` sees Team and Builds but cannot start runs;
- `operator` can start them;
- `editor` can change the automations and sees Restricted;
- `admin` sees everything.

CI runs the seed against the compose package with PostgreSQL (`deploy/test/compose-smoke.sh --seed`). It checks what each user sees, and that a second seed changes nothing.
