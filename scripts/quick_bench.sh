#!/usr/bin/env bash
# =============================================================================
# quick_bench.sh — Fast local benchmark on any OS with existing environment
# =============================================================================
# Usage:
#   chmod +x quick_bench.sh
#   ./quick_bench.sh /path/to/django
#   ./quick_bench.sh /path/to/django /path/to/flask /path/to/requests
#
# Assumes:
#   - pyspector is installed and on PATH (pip install pyspector)
#   - dotnet is installed and on PATH
#   - PySpectorC# is at ../PySpectorC# or set via PYSPECTOR_CSHARP_PATH env var
#     (Repo: https://github.com/MM2010/PySpectorC-)
# =============================================================================

set -euo pipefail

CSHARP_PATH="${PYSPECTOR_CSHARP_PATH:-$(cd "$(dirname "$0")/.." && pwd)}"
OUTPUT_DIR="${OUTPUT_DIR:-./bench_results}"
mkdir -p "$OUTPUT_DIR"

echo "=== PySpector Quick Benchmark ==="
echo "PySpectorC# path: ${CSHARP_PATH}"
echo "Targets: $*"
echo ""

for target in "$@"; do
    if [ ! -d "$target" ]; then
        echo "SKIP: $target not found"
        continue
    fi

    repo_name=$(basename "$target")
    py_count=$(find "$target" -name "*.py" -not -path "*/.git/*" | wc -l)
    loc=$(find "$target" -name "*.py" -not -path "*/.git/*" -exec cat {} + | wc -l)

    echo "--- ${repo_name}: ${py_count} .py files, ${loc} lines ---"

    # Original PySpector
    echo -n "  PySpector (Rust):  "
    start=$(date +%s%N)
    pyspector scan "$target" --format json --severity LOW > "${OUTPUT_DIR}/rust_${repo_name}.json" 2>/dev/null || echo "FAILED"
    end=$(date +%s%N)
    rust_time=$(awk "BEGIN {printf \"%.1f\", ($end - $start) / 1000000000}")
    rust_issues=$(grep -c '"rule_id"' "${OUTPUT_DIR}/rust_${repo_name}.json" 2>/dev/null || echo "0")
    echo "${rust_time}s, ${rust_issues} issues"

    # PySpectorC#
    echo -n "  PySpectorC# (.NET): "
    start=$(date +%s%N)
    dotnet run --project "${CSHARP_PATH}/src/PySpector.Cli" -c Release -- "$target" --format json --severity LOW > "${OUTPUT_DIR}/cs_${repo_name}.json" 2>/dev/null || echo "FAILED"
    end=$(date +%s%N)
    cs_time=$(awk "BEGIN {printf \"%.1f\", ($end - $start) / 1000000000}")
    cs_issues=$(grep -c '"rule_id"' "${OUTPUT_DIR}/cs_${repo_name}.json" 2>/dev/null || echo "0")
    echo "${cs_time}s, ${cs_issues} issues"

    # Parity
    if [ "$rust_issues" -gt 0 ] 2>/dev/null; then
        parity=$(awk "BEGIN {printf \"%.1f\", ($cs_issues/$rust_issues)*100}")
        echo "  Parity: ${parity}%"
    fi
    echo ""
done

echo "Results: ${OUTPUT_DIR}/"
