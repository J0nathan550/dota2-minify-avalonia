#!/bin/bash
set -euo pipefail

# Script to run local CI checks before committing
# Mirrors .github/workflows/ci.yml
cd "$(dirname "${BASH_SOURCE[0]}")/.."

echo "--- Running Pre-commit Checks ---"

echo "[1/6] Syncing dependencies..."
uv sync --group dev

echo "[2/6] Checking Python formatting (ruff)..."
uv run ruff format Minify tests --check

echo "[3/6] Checking JSON/CSS/JS formatting (prettier)..."
npx --yes prettier@3 --check "Minify/**/*.{js,css,json}"

echo "[4/6] Linting Python (ruff)..."
uv run ruff check Minify tests

echo "[5/6] Running Python tests (pytest)..."
uv run pytest

echo "[6/6] Running desktop app tests (dotnet test)..."
dotnet test desktop/Minify.Desktop.slnx

rm -f Minify/logs/warnings.txt
