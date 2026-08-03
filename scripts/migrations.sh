#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_dir/.." && pwd)"
framework="${SPINDLE_EF_FRAMEWORK:-net10.0}"

usage() {
    cat <<'EOF'
Manage migrations for the Spindle EF Core provider projects.

Usage:
  migrations.sh add <provider|all> <migration-name>
  migrations.sh remove [provider|all] [--force]
  migrations.sh list [provider|all]
  migrations.sh check [provider|all]

Providers:
  sqlite, postgresql (or postgres), mysql, sqlserver (or mssql), all

Environment:
  SPINDLE_EF_FRAMEWORK  Target framework used by dotnet ef (default: net10.0)

Examples:
  ./scripts/migrations.sh add all AddFlowPriority
  ./scripts/migrations.sh check postgresql
  ./scripts/migrations.sh remove sqlite
EOF
}

normalize_provider() {
    case "${1,,}" in
        sqlite) echo "Sqlite" ;;
        postgresql|postgres) echo "PostgreSQL" ;;
        mysql) echo "MySql" ;;
        sqlserver|mssql) echo "SqlServer" ;;
        *)
            echo "Unknown provider: $1" >&2
            usage >&2
            exit 2
            ;;
    esac
}

run_for_provider() {
    local provider="$1"
    local command="$2"
    local argument="${3:-}"
    local project="src/Spindle.Persistence.EFCore.${provider}/Spindle.Persistence.EFCore.${provider}.csproj"

    printf '\n==> %s: %s\n' "$provider" "$command"

    case "$command" in
        add)
            dotnet ef migrations add "$argument" \
                --project "$project" \
                --startup-project "$project" \
                --framework "$framework" \
                --output-dir Migrations
            ;;
        remove)
            local remove_arguments=()
            if [[ "$argument" == "--force" ]]; then
                remove_arguments+=(--force)
            fi

            dotnet ef migrations remove \
                "${remove_arguments[@]}" \
                --project "$project" \
                --startup-project "$project" \
                --framework "$framework"
            ;;
        list)
            dotnet ef migrations list \
                --project "$project" \
                --startup-project "$project" \
                --framework "$framework"
            ;;
        check)
            dotnet ef migrations has-pending-model-changes \
                --project "$project" \
                --startup-project "$project" \
                --framework "$framework"
            ;;
    esac
}

command="${1:-help}"
requested_provider="${2:-all}"
argument="${3:-}"

case "$command" in
    help|-h|--help)
        usage
        exit 0
        ;;
    add)
        if [[ -z "$argument" ]]; then
            echo "A migration name is required for the add command." >&2
            usage >&2
            exit 2
        fi
        ;;
    remove)
        if [[ -n "$argument" && "$argument" != "--force" ]]; then
            echo "The remove command only accepts --force as its optional argument." >&2
            usage >&2
            exit 2
        fi
        ;;
    list|check) ;;
    *)
        echo "Unknown command: $command" >&2
        usage >&2
        exit 2
        ;;
esac

cd "$repository_root"

if [[ "${requested_provider,,}" == "all" ]]; then
    for provider in Sqlite PostgreSQL MySql SqlServer; do
        run_for_provider "$provider" "$command" "$argument"
    done
else
    run_for_provider "$(normalize_provider "$requested_provider")" "$command" "$argument"
fi
