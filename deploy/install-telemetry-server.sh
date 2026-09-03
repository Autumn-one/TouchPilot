#!/usr/bin/env bash
set -Eeuo pipefail

repository="Autumn-one/TouchPilot"
branch="main"
install_path="/usr/local/bin/touchpilot-telemetry"
service_user="touchpilot-telemetry"
data_directory="/var/lib/touchpilot-telemetry"
environment_directory="/etc/touchpilot-telemetry"
environment_file="$environment_directory/environment"
service_file="/etc/systemd/system/touchpilot-telemetry.service"

if [[ "${EUID}" -ne 0 ]]; then
    echo "Run this installer as root (for example, pipe it to sudo bash)." >&2
    exit 1
fi
if ! command -v systemctl >/dev/null 2>&1; then
    echo "systemd is required." >&2
    exit 1
fi
if ! command -v curl >/dev/null 2>&1; then
    echo "curl is required." >&2
    exit 1
fi
if ! command -v sha256sum >/dev/null 2>&1; then
    echo "sha256sum is required." >&2
    exit 1
fi

case "$(uname -m)" in
    x86_64|amd64) platform="linux-amd64" ;;
    aarch64|arm64) platform="linux-arm64" ;;
    *) echo "Unsupported CPU architecture: $(uname -m)" >&2; exit 1 ;;
esac

temporary_directory="$(mktemp -d)"
trap 'rm -rf -- "$temporary_directory"' EXIT

download_repository_file() {
    local relative_path="$1"
    local destination="$2"
    local origin="https://raw.githubusercontent.com/$repository/$branch/$relative_path"
    local sources=(
        "$origin"
        "https://ghfast.top/$origin"
        "https://gh-proxy.org/$origin"
    )
    local source
    for source in "${sources[@]}"; do
        if curl --fail --silent --show-error --location \
            --connect-timeout 8 --max-time 90 "$source" --output "$destination"; then
            return 0
        fi
    done
    echo "Unable to download $relative_path from any repository source." >&2
    return 1
}

download_repository_file "distribution/telemetry-server/checksums.sha256" \
    "$temporary_directory/checksums.sha256"
download_repository_file "distribution/telemetry-server/$platform/touchpilot-telemetry" \
    "$temporary_directory/touchpilot-telemetry"

expected_hash="$(awk -v path="$platform/touchpilot-telemetry" '$2 == path { print $1 }' \
    "$temporary_directory/checksums.sha256")"
if [[ ! "$expected_hash" =~ ^[0-9a-f]{64}$ ]]; then
    echo "The repository checksum manifest does not contain $platform." >&2
    exit 1
fi
actual_hash="$(sha256sum "$temporary_directory/touchpilot-telemetry" | awk '{ print $1 }')"
if [[ "$actual_hash" != "$expected_hash" ]]; then
    echo "The telemetry server checksum does not match the repository manifest." >&2
    exit 1
fi

if ! id "$service_user" >/dev/null 2>&1; then
    useradd --system --home-dir "$data_directory" --shell /usr/sbin/nologin "$service_user"
fi
install -d -m 0750 -o "$service_user" -g "$service_user" "$data_directory"
install -d -m 0750 -o root -g "$service_user" "$environment_directory"
if [[ ! -f "$environment_file" ]]; then
    printf '%s\n' \
        'TOUCHPILOT_TELEMETRY_LISTEN=:4318' \
        'TOUCHPILOT_TELEMETRY_DATA_DIR=/var/lib/touchpilot-telemetry' \
        > "$environment_file"
    chmod 0640 "$environment_file"
    chown root:"$service_user" "$environment_file"
fi
install -m 0755 -o root -g root "$temporary_directory/touchpilot-telemetry" "$install_path"

cat > "$service_file" <<'UNIT'
[Unit]
Description=TouchPilot telemetry ingestion service
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=touchpilot-telemetry
Group=touchpilot-telemetry
EnvironmentFile=-/etc/touchpilot-telemetry/environment
ExecStart=/usr/local/bin/touchpilot-telemetry
Restart=on-failure
RestartSec=5s
UMask=0027
NoNewPrivileges=true
PrivateDevices=true
PrivateTmp=true
ProtectClock=true
ProtectControlGroups=true
ProtectHome=true
ProtectHostname=true
ProtectKernelLogs=true
ProtectKernelModules=true
ProtectKernelTunables=true
ProtectSystem=strict
ReadWritePaths=/var/lib/touchpilot-telemetry
RestrictAddressFamilies=AF_INET AF_INET6
RestrictNamespaces=true
RestrictRealtime=true
SystemCallArchitectures=native

[Install]
WantedBy=multi-user.target
UNIT

systemctl daemon-reload
systemctl enable --now touchpilot-telemetry.service
systemctl restart touchpilot-telemetry.service

for _ in {1..20}; do
    if curl --fail --silent --show-error --max-time 2 \
        http://127.0.0.1:4318/healthz >/dev/null; then
        echo "TouchPilot telemetry is running on port 4318."
        exit 0
    fi
    sleep 1
done

systemctl --no-pager --full status touchpilot-telemetry.service >&2 || true
echo "The telemetry service did not become healthy." >&2
exit 1
