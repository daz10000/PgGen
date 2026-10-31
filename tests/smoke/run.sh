#!/usr/bin/env bash
# End-to-end smoke test for the generators:
#   SQL -> fresh database -> generated projects compile against it -> live HTTP round trip.
# Needs a local Postgres where the read_write role (password readwrite, the generated defaults)
# can create databases.  Override psql connection settings with the usual PG* variables.
# Drops and recreates the database pggen_smoke.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$here/../.."
work="$(mktemp -d)"   # outside the repo: the repo's Directory.Packages.props would stop the generated one
trap 'rm -rf "$work"' EXIT

export PGHOST="${PGHOST:-127.0.0.1}" PGUSER="${PGUSER:-read_write}" PGPASSWORD="${PGPASSWORD:-readwrite}"
PSQL="${PSQL:-psql}"

dotnet build "$repo/PgGen.fsproj" -c Debug -v quiet
dotnet fsi "$here/spec.fsx" "$work"

"$PSQL" -d postgres -w -q -c "DROP DATABASE IF EXISTS pggen_smoke" -c "CREATE DATABASE pggen_smoke"
"$PSQL" -d pggen_smoke -w -q -v ON_ERROR_STOP=1 -f "$work/smoke.sql"

dotnet build "$work/out/Api/SmokeApi.fsproj" -v quiet   # shared project: no database needed
cp -r "$here/rt" "$work/rt"
dotnet build "$work/rt/rt.fsproj" -v quiet              # backend: type-checked against pggen_smoke
if [[ -n "${SMOKE_VERBOSE:-}" ]]; then   # SMOKE_VERBOSE=1 shows server logs / exceptions
    dotnet run --no-build --project "$work/rt/rt.fsproj"
else
    dotnet run --no-build --project "$work/rt/rt.fsproj" 2>&1 | grep -E "^(PASS|FAIL|[0-9]+ failures)"
fi
