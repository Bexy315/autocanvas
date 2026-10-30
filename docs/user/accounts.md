---
sidebar_position: 6
---

# Users and groups

AutoCanvas has its own accounts: no external identity provider is needed. Every user signs in with
a user name and a password; administrators manage users and groups, and groups are what folder
permissions are granted to.

## Signing in

The web UI arrives with S3; until then, sign in through the API. The session lasts 7 days without
use and 30 days at most:

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

## The first admin

A fresh installation has no accounts. Create the first admin in one of two ways.

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

As long as there is no account, the server log says how to create one.

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

## Groups

A group has a unique name, an optional description and any number of members. Grant permissions on
folders to groups rather than to individual users, so that people who join or leave a team only
need to be added to or removed from its group. Deleting a group removes its memberships, not its
members.
