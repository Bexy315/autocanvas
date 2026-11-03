# 0019. Check npm licenses against the shared list, with named build-time exceptions

- Status: Accepted
- Date: 2026-11-01 (accepted 2026-11-03)

## Context

The web UI ([ADR-0018](0018-web-ui.md)) brings about 330 npm packages, most of them build and test
tools. The license gate ([ADR-0006](0006-quality-gates.md)) allows only MIT-compatible licenses, and
its list (`build/allowed-licenses.json`) was written for NuGet. The npm ecosystem uses further
permissive licenses: ISC, 0BSD, MIT-0, BlueOak-1.0.0 and CC0-1.0 on many small packages,
Python-2.0 on `argparse`, and CC-BY-3.0 on the SPDX license data the checker itself reads. One
package has a weak copyleft license: `lightningcss` (MPL-2.0), the CSS compiler that Vite 8 and
Tailwind 4 run at build time. It is used unchanged, and nothing of it ends up in the built UI.

## Options

1. **One list for both ecosystems, extended by the permissive licenses, plus named exceptions** for
   packages whose license is not on the list, each with a reason.
2. **Allow MPL-2.0 in general.** Simpler, but every future MPL package would pass without a
   decision, including ones that are shipped.
3. **Avoid `lightningcss`.** Older versions of Vite and Tailwind, or another CSS toolchain. That
   departs from the stack chosen in ADR-0018 for a package that is never shipped.

## Decision

Option 1. `build/allowed-licenses.json` stays the single allow list for NuGet and npm and gains
ISC, 0BSD, MIT-0, BlueOak-1.0.0, CC0-1.0, Python-2.0 and CC-BY-3.0, all permissive. The web UI's
`license-exceptions.json` names the packages allowed with another license: today only
`lightningcss` (and its platform builds) with MPL-2.0, because it is a build tool that is used
unchanged and not shipped. `npm run licenses` checks every installed package, runtime and build,
direct and transitive, and fails on anything else.

## Consequences

- A new package with a license outside the list fails CI until someone decides; an exception needs
  its reason in the file and in `docs/technical/dependencies.md`.
- An exception is tied to a package and its license: if `lightningcss` changes its license, or
  another package arrives with MPL-2.0, the check fails again.
- Revisit if an exception would have to cover code that is shipped with the server or the UI.
