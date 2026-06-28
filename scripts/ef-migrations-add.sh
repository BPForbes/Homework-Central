#!/usr/bin/env bash
# Scaffold an EF migration, then strip auto-generated artifacts.
#
# dotnet ef migrations add emits *.Designer.cs and *ModelSnapshot.cs. This project
# keeps hand-authored migration classes only. The wrapper:
#   1. runs dotnet ef migrations add
#   2. copies [Migration] / [DbContext] attributes onto the main migration .cs file
#   3. deletes *.Designer.cs and *ModelSnapshot.cs
#
# Usage:
#   scripts/ef-migrations-add.sh <MigrationName> [--project path] [--startup-project path]
#
# Examples:
#   scripts/ef-migrations-add.sh AddUserPreferences
#   scripts/ef-migrations-add.sh AddUserPreferences --project backend/HomeworkCentral.Api
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="${EF_PROJECT:-$repo_root/backend/HomeworkCentral.Api}"
startup_project="${EF_STARTUP_PROJECT:-$project}"

if [[ $# -lt 1 ]]; then
  echo "error: migration name required" >&2
  echo "usage: $0 <MigrationName> [--project path] [--startup-project path]" >&2
  exit 1
fi

migration_name="$1"
shift

extra_args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --project)
      if [[ $# -lt 2 ]]; then
        echo "error: --project requires a path" >&2
        echo "usage: $0 <MigrationName> [--project path] [--startup-project path]" >&2
        exit 1
      fi
      project="$2"
      extra_args+=("$1" "$2")
      shift 2
      ;;
    --startup-project)
      if [[ $# -lt 2 ]]; then
        echo "error: --startup-project requires a path" >&2
        echo "usage: $0 <MigrationName> [--project path] [--startup-project path]" >&2
        exit 1
      fi
      startup_project="$2"
      extra_args+=("$1" "$2")
      shift 2
      ;;
    *)
      extra_args+=("$1")
      shift
      ;;
  esac
done

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

dotnet ef migrations add "$migration_name" \
  --project "$project" \
  --startup-project "$startup_project" \
  "${extra_args[@]}"

project_dir="$project"
if [[ "$project_dir" == *.csproj ]]; then
  project_dir="$(dirname "$project_dir")"
fi

migrations_dir="$project_dir/Migrations"

shopt -s nullglob
for designer in "$migrations_dir"/*.Designer.cs; do
  base="${designer%.Designer.cs}"
  main_cs="${base}.cs"
  migration_id="$(basename "$base")"

  if [[ ! -f "$main_cs" ]]; then
    echo "error: expected migration file missing: $main_cs" >&2
    exit 1
  fi

  if ! grep -q '^\[Migration("' "$main_cs"; then
    python3 - "$main_cs" "$migration_id" <<'PY'
import pathlib
import re
import sys

main_cs = pathlib.Path(sys.argv[1])
migration_id = sys.argv[2]
text = main_cs.read_text()

if "[Migration(" in text:
    sys.exit(0)

uses = []
if "AppDbContext" not in text:
    uses.extend([
        "using HomeworkCentral.Api.Data;",
        "using Microsoft.EntityFrameworkCore;",
        "using Microsoft.EntityFrameworkCore.Infrastructure;",
    ])

if uses:
    lines = text.splitlines()
    insert_at = 0
    for i, line in enumerate(lines):
        if line.startswith("using "):
            insert_at = i + 1
    lines[insert_at:insert_at] = uses
    text = "\n".join(lines) + ("\n" if text.endswith("\n") else "")

text = re.sub(
    r"public\s+partial\s+class\s+(\w+)\s*:\s*Migration",
    rf'[DbContext(typeof(AppDbContext))]\n    [Migration("{migration_id}")]\n    public class \1 : Migration',
    text,
    count=1,
)

main_cs.write_text(text)
PY
  fi

  rm -f "$designer"
done

rm -f "$migrations_dir"/*ModelSnapshot.cs

# Rename timestamp-prefixed files to name-only (keep [Migration] id for history).
for stamped in "$migrations_dir"/*_"${migration_name}.cs"; do
  if [[ -f "$stamped" ]]; then
    mv "$stamped" "$migrations_dir/${migration_name}.cs"
    break
  fi
done

echo "Migration scaffolded. Auto-generated *.Designer.cs and *ModelSnapshot.cs were removed."
echo "Review and edit: $migrations_dir/${migration_name}.cs"
