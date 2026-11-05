#!/usr/bin/env bash
# Turns a fresh Ubuntu 24.04 host into the dev instance's host (deploy/dev/README.md, ADR-0023).
# Run it as root from a copy of the repository's deploy directory; running it again is harmless and
# refreshes Cloudflare's address ranges, the scripts and the compose files. It starts no instance:
# the first deploy does.
#
#   deploy/dev/bootstrap.sh --domain <domain> --deploy-key <public key file> \
#     --cert <origin certificate> --key <its private key> [--images ghcr.io/<owner>]
set -euo pipefail

domain=
deploy_key=
cert=
key=
images=ghcr.io/bexy315
while [ $# -gt 0 ]; do
  case "$1" in
    --domain) domain=$2; shift 2 ;;
    --deploy-key) deploy_key=$2; shift 2 ;;
    --cert) cert=$2; shift 2 ;;
    --key) key=$2; shift 2 ;;
    --images) images=${2%/}; shift 2 ;;
    *) echo "bootstrap: unknown argument '$1'" >&2; exit 2 ;;
  esac
done

say() { printf '\n== %s\n' "$*"; }
fail() { echo "bootstrap: $*" >&2; exit 1; }
[ "$(id -u)" = 0 ] || fail "run it as root."
[ -n "$domain" ] || fail "--domain is needed."
[ -f "$deploy_key" ] || fail "--deploy-key must name the deploy key's public key file."
[ -f "$cert" ] && [ -f "$key" ] || fail "--cert and --key must name Cloudflare's origin certificate and its key."
grep -q '^ssh-' "$deploy_key" || fail "$deploy_key is not an SSH public key."

here=$(cd "$(dirname "$0")" && pwd)
compose=$(cd "$here/../compose" && pwd)
home=/opt/autocanvas-dev
instance=$home/instance
export DEBIAN_FRONTEND=noninteractive

say "Packages and security updates"
apt-get update -q
apt-get upgrade -yq
apt-get install -yq ca-certificates curl jq ufw unattended-upgrades fail2ban
# Security updates every day, without reboots (Ubuntu's 50unattended-upgrades decides which).
cat >/etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
systemctl enable --now unattended-upgrades fail2ban >/dev/null

say "Docker"
if ! command -v docker >/dev/null 2>&1; then
  # Docker's own repository, as its installation guide for Ubuntu says.
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  # shellcheck source=/dev/null
  codename=$(. /etc/os-release && echo "$VERSION_CODENAME")
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $codename stable" \
    >/etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -yq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
# Logs that rotate themselves.
daemon='{ "log-driver": "local" }'
if [ "$(cat /etc/docker/daemon.json 2>/dev/null)" != "$daemon" ]; then
  echo "$daemon" >/etc/docker/daemon.json
  systemctl restart docker
fi
systemctl enable --now docker >/dev/null

say "SSH: keys only"
cat >/etc/ssh/sshd_config.d/10-autocanvas-dev.conf <<'EOF'
# Written by deploy/dev/bootstrap.sh. Read before the distribution's files, so these win.
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin prohibit-password
AllowUsers root deploy
EOF
sshd -t
systemctl reload ssh

say "The deploy user"
if ! id deploy >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash --groups docker deploy
fi
usermod --append --groups docker deploy
passwd --lock deploy >/dev/null
# Owned by root, so the deploy user cannot widen what its key may do.
install -d -m 0755 -o root -g root /home/deploy/.ssh
printf 'restrict,command="%s/remote.sh" %s\n' "$home" "$(head -n 1 "$deploy_key")" >/home/deploy/.ssh/authorized_keys
chown root:root /home/deploy/.ssh/authorized_keys
chmod 0644 /home/deploy/.ssh/authorized_keys

say "The instance in $instance"
install -d -m 0755 -o root -g root "$home"
install -m 0755 -o root -g root "$here/remote.sh" "$home/remote.sh"
install -d -m 0750 -o deploy -g deploy "$instance"
install -m 0644 -o deploy -g deploy "$compose/compose.yaml" "$compose/compose.postgres.yaml" \
  "$here/compose.dev.yaml" "$here/Caddyfile" "$instance/"
install -m 0755 -o deploy -g deploy "$here/seed.sh" "$instance/seed.sh"
install -d -m 0700 -o root -g root /etc/autocanvas-dev/origin
install -m 0600 -o root -g root "$cert" /etc/autocanvas-dev/origin/cert.pem
install -m 0600 -o root -g root "$key" /etc/autocanvas-dev/origin/key.pem

if [ ! -f "$instance/.env" ]; then
  gid=$(stat -c %g /var/run/docker.sock)
  password=$(od -An -N24 -tx1 /dev/urandom | tr -d ' \n')
  cat >"$instance/.env" <<EOF
# Written once by deploy/dev/bootstrap.sh; remote.sh sets the image tags on every deploy.
COMPOSE_PROJECT_NAME=autocanvas
COMPOSE_FILE=compose.yaml:compose.postgres.yaml:compose.dev.yaml
DEV_DOMAIN=$domain
AUTOCANVAS_IMAGE_REPOSITORY=$images
AUTOCANVAS_SERVER_IMAGE=$images/autocanvas-server:dev
AUTOCANVAS_RUNNER_IMAGE=$images/autocanvas-runner:dev
AUTOCANVAS_DOCKER_GID=$gid
AUTOCANVAS_POSTGRES_PASSWORD=$password
# Fixed, because the project's name is: where the runner's volume is on this host.
AUTOCANVAS_RUNNER_HOST_DATA=/var/lib/docker/volumes/autocanvas_runner/_data
# Caddy reaches the server through Docker's published port, from a Docker network's gateway.
AUTOCANVAS_TRUSTED_PROXIES=172.16.0.0/12
EOF
  chown deploy:deploy "$instance/.env"
  chmod 0600 "$instance/.env"
fi

say "Firewall: SSH, and HTTPS from Cloudflare only"
v4=$(curl -fsS https://www.cloudflare.com/ips-v4)
v6=$(curl -fsS https://www.cloudflare.com/ips-v6)
[ -n "$v4" ] && [ -n "$v6" ] || fail "could not read Cloudflare's address ranges."
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null
ufw allow 22/tcp comment ssh >/dev/null
# Drop the ranges of an earlier run, highest rule number first, then add today's.
ufw status numbered | sed -n 's/^\[ *\([0-9]*\)\].*# cloudflare$/\1/p' | sort -rn |
  while read -r number; do ufw --force delete "$number" >/dev/null; done
for range in $v4 $v6; do
  ufw allow proto tcp from "$range" to any port 443 comment cloudflare >/dev/null
done
ufw --force enable >/dev/null
ufw status verbose | head -n 5

say "Done"
echo "The host is ready. The first deploy (remote.sh deploy <commit>, through the deploy key) starts the instance."
