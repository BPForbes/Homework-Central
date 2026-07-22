#!/usr/bin/env bash
# Run the same SonarQube analysis CI uses (C# via SonarScanner for .NET + TypeScript via scanAll).
#
# Prerequisites:
#   - .NET SDK matching global.json
#   - Java 17+
#   - Node.js 22+ (frontend npm ci)
#   - SONAR_TOKEN, SONAR_PROJECT_KEY, and (for SonarQube Cloud) SONAR_ORGANIZATION
#
# Usage:
#   export SONAR_TOKEN=...
#   export SONAR_PROJECT_KEY=BPForbes_Homework-Central
#   export SONAR_ORGANIZATION=your-org-key
#   scripts/run-sonar.sh
#
# Optional:
#   SONAR_HOST_URL   default https://sonarcloud.io (use https://sonarqube.us for US Cloud,
#                    or your self-hosted server URL)
#   HC_SKIP_NPM_CI=1 skip frontend npm ci when node_modules is already warm
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

SONAR_HOST_URL="${SONAR_HOST_URL:-https://sonarcloud.io}"
SONAR_PROJECT_KEY="${SONAR_PROJECT_KEY:-}"
SONAR_ORGANIZATION="${SONAR_ORGANIZATION:-}"
SONAR_TOKEN="${SONAR_TOKEN:-}"
SCANNER_DIR="$REPO_ROOT/.sonar/scanner"

if [[ -z "$SONAR_TOKEN" || -z "$SONAR_PROJECT_KEY" ]]; then
  echo "Set SONAR_TOKEN and SONAR_PROJECT_KEY (and SONAR_ORGANIZATION for SonarQube Cloud)." >&2
  echo "See docs/COMMENT_DOCUMENTATION_GUIDE.md#analyzer-and-ci-expectations" >&2
  exit 1
fi

if [[ -z "$SONAR_ORGANIZATION" ]] && { [[ "$SONAR_HOST_URL" == *sonarcloud* ]] || [[ "$SONAR_HOST_URL" == *sonarqube.us* ]]; }; then
  echo "SONAR_ORGANIZATION is required for SonarQube Cloud." >&2
  exit 1
fi

if [[ "${HC_SKIP_NPM_CI:-}" != "1" ]]; then
  (cd frontend && npm ci)
fi

dotnet tool update dotnet-sonarscanner --tool-path "$SCANNER_DIR"

BEGIN_ARGS=(
  "/k:${SONAR_PROJECT_KEY}"
  "/d:sonar.host.url=${SONAR_HOST_URL}"
  "/d:sonar.token=${SONAR_TOKEN}"
  "/d:sonar.scanner.scanAll=true"
  "/d:sonar.qualitygate.wait=true"
  "/d:sonar.exclusions=**/Migrations/**,**/node_modules/**,**/dist/**,**/.vite/**,**/bin/**,**/obj/**,**/.sonar/**"
  "/d:sonar.typescript.tsconfigPaths=frontend/tsconfig.json,frontend/tsconfig.app.json"
)
if [[ -n "$SONAR_ORGANIZATION" ]]; then
  BEGIN_ARGS+=("/o:${SONAR_ORGANIZATION}")
fi

"$SCANNER_DIR/dotnet-sonarscanner" begin "${BEGIN_ARGS[@]}"
dotnet build HomeworkCentral.sln --configuration Release
"$SCANNER_DIR/dotnet-sonarscanner" end "/d:sonar.token=${SONAR_TOKEN}"
