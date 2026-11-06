---
sidebar_position: 6
---

# Users and groups

AutoCanvas has its own accounts: no external identity provider is needed. Every user signs in with
a user name and a password; administrators manage users and groups, and groups are what folder
permissions are granted to.

## Signing in

Open the server's address in a browser and sign in with your user name and password. If an admin
set your password (or it is the first admin's initial one), the web UI asks you to choose your own
before anything else. The session lasts 7 days without use and 30 days at most; when it ends, the
web UI asks you to sign in again and then returns to the page you were on.

Your account page (the menu with your name in the top right) shows your account and groups and lets
you change your password. The same menu switches between a light and a dark color scheme, or
follows your system's setting.

Scripts sign in through the API:

```sh
curl -c cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json'   -d '{"userName":"root","password":"…"}' http://localhost:5080/api/v1/auth/sign-in
curl -b cookies.txt http://localhost:5080/api/v1/me
```

If the password was set by an admin, change it first; until then nothing else is allowed:

```sh
curl -b cookies.txt -X PUT -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json'   -d '{"currentPassword":"…","newPassword":"my own long password"}' http://localhost:5080/api/v1/me/password
```

Changing the password signs you out everywhere else. `GET /api/v1/me/sessions` lists where you are
signed in; `POST /api/v1/auth/sign-out-everywhere` ends all of it. After five wrong passwords in a
row, the user name is locked for 15 minutes.

## Your account

**Account** in the user menu shows your profile and changes your password. It also lists **where you
are signed in** (browser, address, since when), with **Sign out there** for the others, and your
**API tokens**: **New token** shows the token once, with an example call.

## API tokens

Scripts, CI jobs and other tools use a personal API token instead of a password. Create one while
signed in (a token cannot create tokens):

```sh
curl -b cookies.txt -H 'X-AutoCanvas-Request: 1' -H 'Content-Type: application/json'   -d '{"name":"nightly backup","expiresAt":"2027-01-01T00:00:00Z"}' http://localhost:5080/api/v1/me/tokens
```

The answer contains the token (`acp_…`) once; store it right away, it cannot be shown again. Use
it as a bearer token; requests with a token do not need the `X-AutoCanvas-Request` header:

```sh
curl -H 'Authorization: Bearer acp_…' http://localhost:5080/api/v1/me
```

A token can do what its owner can do, and nothing once the owner is disabled. Without
`expiresAt` it never expires. `GET /api/v1/me/tokens` lists your tokens with when they were last
used; `DELETE /api/v1/me/tokens/{id}` revokes one. Admins can list and revoke the tokens of any
user under `/api/v1/users/{id}/tokens`, for example when one has leaked.

## The first admin

A fresh installation has no accounts. Create the first admin in one of three ways.

**In the browser**, with the setup: a server without accounts logs an address such as
`http://localhost:5080/setup?token=…`, and every page of the web UI leads to the setup. On a server
without a browser, open `/setup` from another machine and paste the token from the log. Only
someone who can read the server's log has the token, and it changes when the server restarts. The
setup has four steps:

1. **Token**: taken from the address, or typed in.
2. **Admin**: the first admin's user name, display name, e-mail and password (at least 12
   characters). The admin chose the password, so it need not be changed at the first sign-in.
3. **Addresses**: where people and runners reach the server, filled in from the browser; an address
   set by the server's configuration is shown locked. They can be changed later under
   **Administration** → **Instance**.
4. **Options**: register the runner that came with the package, and create an example automation.

**Set up AutoCanvas** signs the new admin in. If the server generated its master key, the setup
shows it once, to be stored apart from the database
([Operations](../technical/operations.md#master-key)); then it waits for the bundled runner to come
online, which can be skipped.

**From the command line**, on the machine the server runs on, with the same settings as the server
(for example the same `AUTOCANVAS_Database__…` variables):

```sh
dotnet AutoCanvas.Server.dll admin create root
```

It prints a one-time password. Sign in with it and choose your own password; until then, the
account can do nothing else. To type the password yourself instead, pass it on standard input:

```sh
echo 'my long admin password' | dotnet AutoCanvas.Server.dll admin create root --password-stdin
```

**From settings**, for containers and other installations without a terminal: set
`AUTOCANVAS_BOOTSTRAP_ADMIN_USERNAME` and `AUTOCANVAS_BOOTSTRAP_ADMIN_PASSWORD`. When the server
starts and there is no account at all, it creates that admin, who must change the password at the
first sign-in. Once any account exists, the settings are ignored, so you can remove them after the
first start. If the admin cannot be created (for example because the password is too short), the
server does not start and says why.

As long as there is no account, the server log shows the setup address (or, with
`Setup:Enabled` set to `false`, how to create the first admin).

## Locked out

If nobody can sign in as an admin any more, give an admin a new one-time password from the command
line. This also ends all their sessions:

```sh
dotnet AutoCanvas.Server.dll admin reset-password root
```

## User names and passwords

- **User names** have 1 to 64 characters: letters, digits, `.`, `_` and `-`, starting with a
  letter or digit. They are unique regardless of case: `Ada` and `ada` are the same user, and the
  user signs in with either spelling.
- **Passwords** have 12 to 256 characters and must not be the user name. There are no rules about
  digits or symbols: a long passphrase is better than a short complicated password.
- A user also has a **display name**, shown to others, and optionally an **e-mail address**, which
  is information only: AutoCanvas sends no e-mail.

## Administrators

Admins manage users and groups and see everything in the instance. There is always at least one
enabled admin: the last one cannot be demoted, disabled or deleted.

When an admin creates a user or sets a new password for them, the user must change that password
at their next sign-in, so the admin never knows the password in use. Disabling a user signs them
out everywhere; deleting a user also removes their group memberships. The audit log keeps every
change with the name the user had at the time.

In the web UI, **Administration** (in the sidebar for admins) has **Users** and **Groups** next to the
instance's variables and secrets:

- **New user**: the browser makes up a first password and shows it once, to pass on; the user
  replaces it at the first sign-in.
- **Per user**: **Change** (display name, e-mail, admin, disabled), **Reset password** (again a
  password made up and shown once), **API tokens** (to revoke a leaked one) and **Delete**.
- **Groups**: created, renamed and deleted. A group's members are added by finding them by name.

## Finding people and groups

Sharing a folder needs the names of people and groups, so everyone signed in may search them:
`GET /api/v1/directory?query=ad` finds users (not disabled) whose user name or display name contains
the query, and groups whose name does; names that start with it come first, at most 20 of each. The
query needs two characters at least, and only names come back, never e-mail addresses. Listing all
users and groups stays for admins.

Admins manage users under **Administration** → **Users**: creating them with a first password,
resetting passwords, making admins and deactivating.

![The users of an instance](img/admin-users-light.png#gh-light-mode-only)
![The users of an instance](img/admin-users-dark.png#gh-dark-mode-only)

## Groups

A group has a unique name, an optional description and any number of members. Grant permissions on
folders to groups rather than to individual users, so that people who join or leave a team only
need to be added to or removed from its group. Deleting a group removes its memberships, not its
members.

**Administration** → **Groups** creates groups; choosing one manages its members.

![Adding a member to a group](img/admin-group-members-light.png#gh-light-mode-only)
![Adding a member to a group](img/admin-group-members-dark.png#gh-dark-mode-only)

## See also

- [Folders and permissions](folders.md): giving users and groups roles.
- [Security](../technical/security.md): sessions, tokens and passwords.
- [Audit log](../technical/audit.md): what is recorded about accounts.
