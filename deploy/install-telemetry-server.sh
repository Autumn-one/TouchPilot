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
service_name="touchpilot-telemetry.service"
temporary_directory=""
staged_binary=""
staged_unit=""
replacement_started=false
had_binary=false
had_unit=false
was_active=false
was_enabled=false

download_repository_file() {
    local relative_path="$1" destination="$2" expected_hash="${3:-}"
    local origin="https://raw.githubusercontent.com/$repository/$branch/$relative_path"
    local sources=("$origin" "https://ghfast.top/$origin" "https://gh-proxy.org/$origin")
    local source actual_hash
    for source in "${sources[@]}"; do
        if ! curl --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
            --connect-timeout 8 --max-time 90 "$source" --output "$destination"; then
            continue
        fi
        if [[ -n "$expected_hash" ]]; then
            actual_hash="$(sha256sum "$destination" | awk '{ print $1 }')"
            if [[ "$actual_hash" != "$expected_hash" ]]; then
                echo "Checksum mismatch from $source; trying the next source." >&2
                continue
            fi
        else
            local manifest_hash
            manifest_hash="$(awk -v path="$platform/touchpilot-telemetry" '$2 == path { print $1 }' "$destination")"
            if [[ ! "$manifest_hash" =~ ^[0-9a-f]{64}$ ]]; then
                echo "Invalid checksum manifest from $source; trying the next source." >&2
                continue
            fi
        fi
        return 0
    done
    echo "Unable to verify $relative_path from any repository source." >&2
    return 1
}

write_unit() {
    cat > "$1" <<'UNIT'
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
}

health_url() {
    if [[ -n "${TOUCHPILOT_TELEMETRY_HEALTH_URL:-}" ]]; then
        printf '%s\n' "$TOUCHPILOT_TELEMETRY_HEALTH_URL"
        return
    fi
    local line listen=':4318' host port
    while IFS= read -r line || [[ -n "$line" ]]; do
        case "$line" in
            TOUCHPILOT_TELEMETRY_LISTEN=*)
                listen="${line#*=}"
                listen="${listen%$'\r'}"
                listen="${listen#\"}"; listen="${listen%\"}"
                listen="${listen#\'}"; listen="${listen%\'}"
                ;;
        esac
    done < "$environment_file"
    port="${listen##*:}"
    host="${listen%:*}"
    case "$host" in ''|'0.0.0.0'|'[::]') host='127.0.0.1' ;; esac
    if [[ ! "$port" =~ ^[0-9]{1,5}$ ]] || (( 10#$port < 1 || 10#$port > 65535 )); then
        echo "Set TOUCHPILOT_TELEMETRY_HEALTH_URL for this listen configuration." >&2
        return 1
    fi
    printf 'http://%s:%s/healthz\n' "$host" "$port"
}

wait_healthy() {
    local url attempt body
    url="$(health_url)" || return 1
    for attempt in {1..20}; do
        if systemctl is-active --quiet "$service_name" &&
            body="$(curl --fail --silent --show-error --max-time 2 "$url")" &&
            [[ "$body" =~ \"status\"[[:space:]]*:[[:space:]]*\"ok\" ]]; then
            return 0
        fi
        sleep 1
    done
    return 1
}

rollback() {
    systemctl stop "$service_name" || return 1
    if $had_binary; then
        staged_binary="$(mktemp "${install_path}.new.XXXXXX")" || return 1
        cp -p -- "$temporary_directory/previous-binary" "$staged_binary" || return 1
        mv -f -- "$staged_binary" "$install_path" || return 1
    else
        rm -f -- "$install_path" || return 1
    fi
    if $had_unit; then
        staged_unit="$(mktemp "${service_file}.new.XXXXXX")" || return 1
        cp -p -- "$temporary_directory/previous-unit" "$staged_unit" || return 1
        mv -f -- "$staged_unit" "$service_file" || return 1
    else
        rm -f -- "$service_file" || return 1
    fi
    systemctl daemon-reload || return 1
    if $was_enabled; then
        systemctl enable "$service_name" || return 1
    else
        systemctl disable "$service_name" || return 1
    fi
    if $was_active; then
        systemctl start "$service_name" || return 1
        wait_healthy || return 1
    fi
    echo "The previous installation was restored." >&2
}

cleanup() {
    local status=$?
    trap - EXIT
    if (( status != 0 )) && $replacement_started; then
        if ! rollback; then
            echo "Automatic recovery failed. Backups remain in $temporary_directory." >&2
            exit 1
        fi
    fi
    [[ -z "$staged_binary" ]] || rm -f -- "$staged_binary"
    [[ -z "$staged_unit" ]] || rm -f -- "$staged_unit"
    [[ -z "$temporary_directory" ]] || rm -rf -- "$temporary_directory"
    exit "$status"
}

install_service() {
    if [[ -f "$install_path" ]]; then
        cp -p -- "$install_path" "$temporary_directory/previous-binary"
        had_binary=true
    fi
    if [[ -f "$service_file" ]]; then
        cp -p -- "$service_file" "$temporary_directory/previous-unit"
        had_unit=true
    fi
    if systemctl is-active --quiet "$service_name"; then was_active=true; fi
    if systemctl is-enabled --quiet "$service_name"; then was_enabled=true; fi

    staged_binary="$(mktemp "${install_path}.new.XXXXXX")"
    staged_unit="$(mktemp "${service_file}.new.XXXXXX")"
    install -m 0755 -- "$temporary_directory/touchpilot-telemetry" "$staged_binary"
    write_unit "$staged_unit"
    chmod 0644 "$staged_unit"
    replacement_started=true
    mv -f -- "$staged_binary" "$install_path"
    mv -f -- "$staged_unit" "$service_file"
    systemctl daemon-reload
    systemctl enable "$service_name"
    systemctl restart "$service_name"
    if ! wait_healthy; then
        systemctl --no-pager --full status "$service_name" >&2 || true
        echo "The telemetry service did not become healthy." >&2
        return 1
    fi
}

main() {
    if [[ "$EUID" -ne 0 ]]; then
        echo "Run this installer as root (for example, pipe it to sudo bash)." >&2
        return 1
    fi
    local command
    for command in systemctl curl sha256sum awk install mktemp flock; do
        command -v "$command" >/dev/null || { echo "$command is required." >&2; return 1; }
    done
    exec 9>/run/lock/touchpilot-telemetry-install.lock
    flock -n 9 || { echo "Another installation is running." >&2; return 1; }
    case "$(uname -m)" in
        x86_64|amd64) platform="linux-amd64" ;;
        aarch64|arm64) platform="linux-arm64" ;;
        *) echo "Unsupported CPU architecture: $(uname -m)" >&2; return 1 ;;
    esac

    temporary_directory="$(mktemp -d)"
    trap cleanup EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    download_repository_file "distribution/telemetry-server/checksums.sha256" \
        "$temporary_directory/checksums.sha256"
    local expected_hash
    expected_hash="$(awk -v path="$platform/touchpilot-telemetry" '$2 == path { print $1 }' \
        "$temporary_directory/checksums.sha256")"
    download_repository_file "distribution/telemetry-server/$platform/touchpilot-telemetry" \
        "$temporary_directory/touchpilot-telemetry" "$expected_hash"

    if ! id "$service_user" >/dev/null 2>&1; then
        useradd --system --home-dir "$data_directory" --shell /usr/sbin/nologin "$service_user"
    fi
    install -d -m 0750 -o "$service_user" -g "$service_user" "$data_directory"
    install -d -m 0750 -o root -g "$service_user" "$environment_directory"
    if [[ ! -f "$environment_file" ]]; then
        printf '%s\n' 'TOUCHPILOT_TELEMETRY_LISTEN=:4318' \
            'TOUCHPILOT_TELEMETRY_DATA_DIR=/var/lib/touchpilot-telemetry' > "$environment_file"
        chmod 0640 "$environment_file"
        chown root:"$service_user" "$environment_file"
    fi
    install_service
    echo "TouchPilot telemetry is running: $(health_url)"
}

if [[ "${BASH_SOURCE[0]:-}" == "$0" || -z "${BASH_SOURCE[0]:-}" ]]; then
    main "$@"
fi
