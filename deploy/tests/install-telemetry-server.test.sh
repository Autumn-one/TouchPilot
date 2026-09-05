#!/usr/bin/env bash
set -Eeuo pipefail

test_script="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
installer="$(dirname -- "$test_script")/../install-telemetry-server.sh"

if [[ "${1:-}" == child ]]; then
    mode="$2"
    fixture="$3"
    source "$installer"
    install_path="$fixture/server"
    service_file="$fixture/service"
    environment_file="$fixture/environment"
    temporary_directory="$fixture/work"
    mkdir -p -- "$temporary_directory"
    printf 'new' > "$temporary_directory/touchpilot-telemetry"
    printf 'TOUCHPILOT_TELEMETRY_LISTEN=127.0.0.1:54318\n' > "$environment_file"
    if [[ "$mode" != fresh-failure ]]; then
        printf 'old' > "$install_path"
        printf 'old-unit' > "$service_file"
        touch "$fixture/active" "$fixture/enabled"
    fi
    systemctl() {
        printf '%s\n' "$*" >> "$fixture/calls"
        case "$1" in
            is-active) [[ -f "$fixture/active" ]] ;;
            is-enabled) [[ -f "$fixture/enabled" ]] ;;
            enable) touch "$fixture/enabled" ;;
            disable) rm -f -- "$fixture/enabled" ;;
            stop) rm -f -- "$fixture/active" ;;
            start) touch "$fixture/active" ;;
            restart)
                if [[ "$mode" == restart-failure || "$mode" == fresh-failure ]]; then return 1; fi
                touch "$fixture/active"
                ;;
            *) return 0 ;;
        esac
    }
    curl() {
        if [[ "$mode" == health-failure && "$(< "$install_path")" == new ]]; then return 22; fi
        printf '{"status":"ok","version":"1.0.0"}\n'
    }
    sleep() { :; }
    [[ "$(health_url)" == 'http://127.0.0.1:54318/healthz' ]]
    trap cleanup EXIT
    install_service
    exit 0
fi

fixture_root="$(mktemp -d)"
trap 'rm -rf -- "$fixture_root"' EXIT
for mode in success restart-failure health-failure fresh-failure; do
    fixture="$fixture_root/$mode"
    mkdir -- "$fixture"
    result=0
    bash "$test_script" child "$mode" "$fixture" || result=$?
    if [[ "$mode" == success ]]; then
        [[ "$result" == 0 && "$(< "$fixture/server")" == new ]]
        [[ -f "$fixture/active" && -f "$fixture/enabled" ]]
    elif [[ "$mode" == fresh-failure ]]; then
        [[ "$result" != 0 && ! -f "$fixture/server" && ! -f "$fixture/service" ]]
        [[ ! -f "$fixture/active" && ! -f "$fixture/enabled" ]]
    else
        [[ "$result" != 0 && "$(< "$fixture/server")" == old ]]
        [[ "$(< "$fixture/service")" == old-unit ]]
        [[ -f "$fixture/active" && -f "$fixture/enabled" ]]
    fi
    [[ ! -d "$fixture/work" ]]
    [[ "$(< "$fixture/environment")" == 'TOUCHPILOT_TELEMETRY_LISTEN=127.0.0.1:54318' ]]
    printf 'PASS %s\n' "$mode"
done

(
    source "$installer"
    platform=linux-amd64
    curl() {
        local destination url
        while (( $# )); do
            case "$1" in
                --output) destination="$2"; shift 2 ;;
                https://*) url="$1"; shift ;;
                *) shift ;;
            esac
        done
        printf '%s\n' "$url" >> "$fixture_root/downloads"
        if [[ "$url" == https://raw.githubusercontent.com/* ]]; then
            printf 'bad' > "$destination"
        else
            printf 'good' > "$destination"
        fi
    }
    expected_hash="$(printf 'good' | sha256sum | awk '{ print $1 }')"
    download_repository_file 'distribution/telemetry-server/linux-amd64/touchpilot-telemetry' \
        "$fixture_root/download" "$expected_hash"
    [[ "$(< "$fixture_root/download")" == good ]]
    [[ "$(wc -l < "$fixture_root/downloads")" -eq 2 ]]
    printf 'PASS checksum-mirror-fallback\n'
)
