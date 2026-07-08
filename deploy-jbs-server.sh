#!/usr/bin/env bash
set -Eeuo pipefail

# One-click Ubuntu deployment for BazaarPlusPlus JBS server.
#
# Usage:
#   sudo bash deploy-jbs-server.sh
#   sudo PORT=8787 bash deploy-jbs-server.sh
#   sudo REPO_URL=https://github.com/your/repo.git BRANCH=main bash deploy-jbs-server.sh
#
# Defaults deploy bazzarplusplus-jbs-server-mod to /opt/bazaarplusplus-jbs-server
# and manages it with systemd service bazaarplusplus-jbs-server.service.

APP_NAME="${APP_NAME:-bazaarplusplus-jbs-server}"
SERVICE_NAME="${SERVICE_NAME:-bazaarplusplus-jbs-server}"
APP_DIR="${APP_DIR:-/opt/${APP_NAME}}"
DEPLOY_USER="${DEPLOY_USER:-${APP_NAME}}"
PORT="${PORT:-8787}"
LISTEN_ADDR="${LISTEN_ADDR:-:${PORT}}"
REPO_URL="${REPO_URL:-}"
BRANCH="${BRANCH:-main}"
SOURCE_DIR="${SOURCE_DIR:-}"
FIREWALL="${FIREWALL:-auto}"

log() {
  printf '\033[1;32m[deploy]\033[0m %s\n' "$*"
}

warn() {
  printf '\033[1;33m[warn]\033[0m %s\n' "$*" >&2
}

die() {
  printf '\033[1;31m[error]\033[0m %s\n' "$*" >&2
  exit 1
}

need_root() {
  if [ "$(id -u)" -ne 0 ]; then
    die "Please run as root: sudo bash $0"
  fi
}

detect_source_dir() {
  if [ -n "$SOURCE_DIR" ]; then
    [ -d "$SOURCE_DIR/bazzarplusplus-jbs-server-mod/jbs-server" ] || die "SOURCE_DIR is not a BazaarPlusPlus repo: $SOURCE_DIR"
    printf '%s\n' "$SOURCE_DIR"
    return
  fi

  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  if [ -d "$script_dir/bazzarplusplus-jbs-server-mod/jbs-server" ]; then
    printf '%s\n' "$script_dir"
    return
  fi

  if [ -d "$PWD/bazzarplusplus-jbs-server-mod/jbs-server" ]; then
    printf '%s\n' "$PWD"
    return
  fi

  printf '\n'
}

install_packages() {
  log "Installing system packages"
  apt-get update
  DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    git \
    rsync \
    golang-go
}

ensure_go() {
  if ! command -v go >/dev/null 2>&1; then
    die "Go was not installed successfully"
  fi

  local version
  version="$(go env GOVERSION 2>/dev/null || true)"
  if [[ "$version" =~ ^go([0-9]+)\.([0-9]+) ]]; then
    local major="${BASH_REMATCH[1]}"
    local minor="${BASH_REMATCH[2]}"
    if [ "$major" -lt 1 ] || { [ "$major" -eq 1 ] && [ "$minor" -lt 21 ]; }; then
      die "Go 1.21+ is required, but apt installed $version. Use Ubuntu 24.04+ or install a newer Go toolchain."
    fi
  fi
  log "Using ${version:-go}"
}

prepare_user_and_dirs() {
  if ! id "$DEPLOY_USER" >/dev/null 2>&1; then
    log "Creating system user $DEPLOY_USER"
    useradd --system --home-dir "$APP_DIR" --shell /usr/sbin/nologin "$DEPLOY_USER"
  fi

  mkdir -p "$APP_DIR"
  chown -R "$DEPLOY_USER:$DEPLOY_USER" "$APP_DIR"
}

sync_source() {
  local src="$1"
  local release_dir="$APP_DIR/current"

  rm -rf "$APP_DIR/.build"
  mkdir -p "$APP_DIR/.build"

  if [ -n "$src" ]; then
    log "Copying local source from $src"
    rsync -a --delete \
      --exclude '.git' \
      --exclude 'bazaarplusplus-mod' \
      --exclude 'bazaarplusplus-installer' \
      --exclude 'tests' \
      "$src/bazzarplusplus-jbs-server-mod/" "$APP_DIR/.build/"
  else
    [ -n "$REPO_URL" ] || die "No local source found. Run from the repo root or set REPO_URL=https://..."
    log "Cloning $REPO_URL ($BRANCH)"
    rm -rf "$APP_DIR/.repo"
    git clone --depth 1 --branch "$BRANCH" "$REPO_URL" "$APP_DIR/.repo"
    rsync -a --delete "$APP_DIR/.repo/bazzarplusplus-jbs-server-mod/" "$APP_DIR/.build/"
    rm -rf "$APP_DIR/.repo"
  fi

  [ -f "$APP_DIR/.build/jbs-server/main.go" ] || die "Missing jbs-server/main.go after source sync"
  [ -f "$APP_DIR/.build/web/dashboard.html" ] || warn "web/dashboard.html not found; dashboard route may return 404"

  rm -rf "$release_dir"
  mv "$APP_DIR/.build" "$release_dir"
  chown -R "$DEPLOY_USER:$DEPLOY_USER" "$release_dir"
}

build_binary() {
  log "Building JBS server"
  cd "$APP_DIR/current/jbs-server"
  runuser -u "$DEPLOY_USER" -- env HOME="$APP_DIR" go mod download
  runuser -u "$DEPLOY_USER" -- env HOME="$APP_DIR" go build -trimpath -ldflags="-s -w" -o jbs-server .
  chmod 0755 jbs-server
  mkdir -p logs
  chown -R "$DEPLOY_USER:$DEPLOY_USER" logs
}

write_systemd_service() {
  log "Writing systemd service"
  cat >/etc/systemd/system/"$SERVICE_NAME".service <<EOF
[Unit]
Description=BazaarPlusPlus JBS Server
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=$DEPLOY_USER
Group=$DEPLOY_USER
WorkingDirectory=$APP_DIR/current/jbs-server
ExecStart=$APP_DIR/current/jbs-server/jbs-server -addr $LISTEN_ADDR -error-log logs/error.log
Restart=always
RestartSec=3
Environment=GIN_MODE=release
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ReadWritePaths=$APP_DIR

[Install]
WantedBy=multi-user.target
EOF

  systemctl daemon-reload
  systemctl enable "$SERVICE_NAME"
}

open_firewall() {
  case "$FIREWALL" in
    off|false|0)
      log "Skipping firewall changes"
      return
      ;;
    auto|on|true|1)
      ;;
    *)
      die "Invalid FIREWALL value: $FIREWALL"
      ;;
  esac

  if command -v ufw >/dev/null 2>&1 && ufw status | grep -q "Status: active"; then
    log "Opening TCP port $PORT in ufw"
    ufw allow "$PORT"/tcp
  else
    warn "ufw is not active; if your cloud provider has a security group, open TCP $PORT there"
  fi
}

restart_and_check() {
  log "Starting $SERVICE_NAME"
  systemctl restart "$SERVICE_NAME"
  sleep 2

  if ! systemctl is-active --quiet "$SERVICE_NAME"; then
    journalctl -u "$SERVICE_NAME" -n 80 --no-pager >&2 || true
    die "$SERVICE_NAME failed to start"
  fi

  if curl -fsS "http://127.0.0.1:${PORT}/stats" >/dev/null; then
    log "Health check passed: http://127.0.0.1:${PORT}/stats"
  else
    warn "Service is running, but /stats health check did not respond"
  fi
}

print_next_steps() {
  local public_ip
  public_ip="$(curl -fsS --max-time 2 https://api.ipify.org 2>/dev/null || true)"

  cat <<EOF

Deployment complete.

Service:
  systemctl status $SERVICE_NAME --no-pager
  journalctl -u $SERVICE_NAME -f
  systemctl restart $SERVICE_NAME

Local URLs:
  http://127.0.0.1:$PORT/stats
  http://127.0.0.1:$PORT/dashboard
EOF

  if [ -n "$public_ip" ]; then
    cat <<EOF

Public URLs:
  http://$public_ip:$PORT/stats
  http://$public_ip:$PORT/dashboard
EOF
  fi

  cat <<EOF

If you use a cloud server, also open TCP $PORT in the cloud security group.

EOF
}

main() {
  need_root

  if ! command -v apt-get >/dev/null 2>&1; then
    die "This script is intended for Ubuntu/Debian servers with apt-get"
  fi

  local src
  src="$(detect_source_dir)"

  install_packages
  ensure_go
  prepare_user_and_dirs
  sync_source "$src"
  build_binary
  write_systemd_service
  open_firewall
  restart_and_check
  print_next_steps
}

main "$@"
