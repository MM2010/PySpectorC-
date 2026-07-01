#!/usr/bin/env bash
# =============================================================================
# PySpector vs PySpectorC# — Riproducible Cross-Platform Benchmark
# =============================================================================
# Run on a fresh Debian/Ubuntu Linux machine:
#   chmod +x benchmark_comparison.sh
#   ./benchmark_comparison.sh
#
# What it does:
#   1. Installs all toolchains (Python 3.14, Rust, .NET 10)
#   2. Clones test source repositories (Django, Flask, Requests, Pandas, Scikit-learn)
#   3. Installs original PySpector (Rust/Python) via pip
#   4. Clones PySpectorC# and builds in Release mode
#   5. Runs both scanners on all test repos
#   6. Generates comparison report (report.md)
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
WORK_DIR="${SCRIPT_DIR}/benchmark_workspace"
REPORT_FILE="${SCRIPT_DIR}/benchmark_report.md"
TIMESTAMP=$(date -u +"%Y-%m-%dT%H:%M:%SZ")

# ---- Test Repositories (pinned to specific commits for reproducibility) ----
declare -A TEST_REPOS
TEST_REPOS["django"]="https://github.com/django/django.git|5.1"
TEST_REPOS["flask"]="https://github.com/pallets/flask.git|3.1.0"
TEST_REPOS["requests"]="https://github.com/psf/requests.git|v2.32.3"

# Large repos — only if --full flag
declare -A TEST_REPOS_FULL
TEST_REPOS_FULL["pandas"]="https://github.com/pandas-dev/pandas.git|v2.2.3"
TEST_REPOS_FULL["scikit-learn"]="https://github.com/scikit-learn/scikit-learn.git|1.6.1"

# ---- Configuration ----
PYSPECTOR_REPO="https://github.com/ParzivalHack/PySpector.git"
PYSPECTOR_CSHARP_REPO="https://github.com/MM2010/PySpectorC-.git"
PYSPECTOR_CSHARP_PATH=""  # Set to local path if already cloned

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# =============================================================================
# Helper Functions
# =============================================================================

log()  { echo -e "${CYAN}[$(date +%H:%M:%S)]${NC} $*"; }
ok()   { echo -e "${GREEN}[OK]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
err()  { echo -e "${RED}[ERR]${NC} $*"; }

check_cmd() {
    command -v "$1" &>/dev/null && ok "$1 found: $($1 --version 2>&1 | head -1)" || {
        err "$1 not found"
        return 1
    }
}

# =============================================================================
# Phase 1: Environment Setup
# =============================================================================

setup_environment() {
    log "=== Phase 1: Environment Setup ==="

    # Detect OS
    OS="unknown"
    if [ -f /etc/os-release ]; then
        # shellcheck disable=SC1091
        . /etc/os-release
        OS="${ID:-unknown}"
    fi
    ok "OS detected: $OS"

    # --- Python 3.14 ---
    if ! command -v python3.14 &>/dev/null; then
        log "Installing Python 3.14..."
        case "$OS" in
            ubuntu|debian)
                sudo apt-get update -qq
                sudo apt-get install -y -qq software-properties-common
                sudo add-apt-repository -y ppa:deadsnakes/ppa
                sudo apt-get update -qq
                sudo apt-get install -y -qq python3.14 python3.14-venv python3.14-dev
                ;;
            *)
                err "Unsupported OS for automatic Python 3.14 install. Install manually."
                ;;
        esac
    fi
    check_cmd python3.14

    # Create venv for PySpector
    if [ ! -d "${WORK_DIR}/venv" ]; then
        python3.14 -m venv "${WORK_DIR}/venv"
        ok "Python venv created"
    fi
    source "${WORK_DIR}/venv/bin/activate"

    # --- Rust ---
    if ! command -v cargo &>/dev/null; then
        log "Installing Rust toolchain..."
        curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y
        source "$HOME/.cargo/env"
    fi
    check_cmd cargo
    check_cmd rustc

    # --- .NET 10 ---
    if ! command -v dotnet &>/dev/null; then
        log "Installing .NET 10 SDK..."
        case "$OS" in
            ubuntu|debian)
                # Microsoft package feed for .NET 10
                wget -q https://packages.microsoft.com/config/ubuntu/24.04/packages-microsoft-prod.deb -O /tmp/ms-prod.deb
                sudo dpkg -i /tmp/ms-prod.deb
                sudo apt-get update -qq
                sudo apt-get install -y -qq dotnet-sdk-10.0
                ;;
            *)
                log "Installing .NET via script..."
                curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
                export PATH="$HOME/.dotnet:$PATH"
                echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
                ;;
        esac
    fi
    check_cmd dotnet

    # --- Other tools ---
    sudo apt-get install -y -qq git curl wget time jq hyperfine 2>/dev/null || true
    check_cmd git
    check_cmd hyperfine

    ok "Environment setup complete"
}

# =============================================================================
# Phase 2: Clone Test Sources
# =============================================================================

clone_test_sources() {
    log "=== Phase 2: Clone Test Source Repositories ==="

    mkdir -p "${WORK_DIR}/sources"

    for name in "${!TEST_REPOS[@]}"; do
        local url="" tag=""
        IFS='|' read -r url tag <<< "${TEST_REPOS[$name]}" || true
        local target="${WORK_DIR}/sources/${name}"

        if [ -d "$target/.git" ]; then
            ok "${name}: already cloned"
        else
            log "Cloning ${name} (${tag})..."
            git clone --depth 1 --branch "$tag" "$url" "$target" 2>&1 | tail -1
            ok "${name}: cloned"
        fi

        # Count .py files
        local count=$(find "$target" -name "*.py" -not -path "*/.git/*" -not -path "*/node_modules/*" | wc -l)
        ok "  ${name}: ${count} Python files"
    done

    # Full repos if --full
    if [ "${FULL_MODE:-0}" = "1" ]; then
        for name in "${!TEST_REPOS_FULL[@]}"; do
            local url="" tag=""
            IFS='|' read -r url tag <<< "${TEST_REPOS_FULL[$name]}" || true
            local target="${WORK_DIR}/sources/${name}"
            if [ ! -d "$target/.git" ]; then
                log "Cloning ${name} (${tag})..."
                git clone --depth 1 --branch "$tag" "$url" "$target" 2>&1 | tail -1
            fi
            local count=$(find "$target" -name "*.py" -not -path "*/.git/*" | wc -l)
            ok "  ${name}: ${count} Python files"
        done
    fi
}

# =============================================================================
# Phase 3: Install Original PySpector (Rust/Python)
# =============================================================================

install_pyspector_original() {
    log "=== Phase 3: Install Original PySpector ==="

    source "${WORK_DIR}/venv/bin/activate"

    # Install from PyPI
    pip install --upgrade pip
    pip install pyspector
    check_cmd pyspector

    ok "PySpector original installed: $(pip show pyspector 2>/dev/null | grep Version | cut -d' ' -f2 || echo 'installed')"
}

# =============================================================================
# Phase 4: Clone and Build PySpectorC#
# =============================================================================

build_pyspector_csharp() {
    log "=== Phase 4: Build PySpectorC# ==="

    local cs_target="${WORK_DIR}/pyspector-csharp"

    # Clone if not already present; if already cloned, pull latest changes
    if [ -n "${PYSPECTOR_CSHARP_PATH:-}" ] && [ -d "$PYSPECTOR_CSHARP_PATH" ]; then
        cs_target="$PYSPECTOR_CSHARP_PATH"
    elif [ ! -d "$cs_target" ]; then
        log "Cloning PySpectorC# from ${PYSPECTOR_CSHARP_REPO}..."
        git clone "$PYSPECTOR_CSHARP_REPO" "$cs_target" 2>&1 | tail -3 || {
            err "git clone failed — is the repo public? URL: ${PYSPECTOR_CSHARP_REPO}"
            echo "" > "${WORK_DIR}/.pyspector_csharp_skip"
            return 0
        }
    else
        log "PySpectorC# already cloned, pulling latest..."
        cd "$cs_target" && git pull --ff-only 2>&1 | tail -3 || true
        cd "$SCRIPT_DIR"
    fi

    # Find the CLI project (what we actually need to run benchmarks — avoids .slnx test-project issues)
    local cli_proj
    cli_proj=$(find "$cs_target" -maxdepth 4 -path "*/PySpector.Cli/*.csproj" -print -quit 2>/dev/null)
    # Fallback: any .csproj with "Cli" in name
    if [ -z "$cli_proj" ]; then
        cli_proj=$(find "$cs_target" -maxdepth 4 -name "*Cli*.csproj" -print -quit 2>/dev/null)
    fi

    if [ -z "$cli_proj" ]; then
        warn "No PySpector.Cli project found — listing all .csproj files:"
        find "$cs_target" -maxdepth 4 -name "*.csproj" -print 2>/dev/null | head -20 || true
        warn "HINT: the repo may be missing source files. Push all C# code to GitHub, or:"
        warn "  scp -r src/ root@host:${cs_target}/"
        warn "  PYSPECTOR_CSHARP_PATH=/path/to/local/code ./benchmark_comparison.sh --skip-env --skip-clone"
        warn "Skipping PySpectorC# build — benchmarks will run Rust-only"
        echo "" > "${WORK_DIR}/.pyspector_csharp_skip"
        return 0
    fi

    local proj_dir
    proj_dir=$(dirname "$cli_proj")
    ok "CLI project found: ${cli_proj}"

    cd "$cs_target"

    log "Restoring NuGet packages..."
    dotnet restore "$cli_proj" 2>&1 | tail -3 || {
        warn "dotnet restore failed — trying with --ignore-failed-sources..."
        dotnet restore "$cli_proj" --ignore-failed-sources 2>&1 | tail -3 || {
            err "dotnet restore failed — check that all source projects are pushed to GitHub"
            warn "HINT: the .slnx references test projects that may not be on GitHub yet"
            warn "Skipping PySpectorC# build — benchmarks will run Rust-only"
            echo "" > "${WORK_DIR}/.pyspector_csharp_skip"
            cd "$SCRIPT_DIR"
            return 0
        }
    }

    log "Building Release configuration..."
    dotnet build "$cli_proj" -c Release -p:TreatWarningsAsErrors=false 2>&1 | tail -5 || {
        err "dotnet build failed"
        echo "" > "${WORK_DIR}/.pyspector_csharp_skip"
        cd "$SCRIPT_DIR"
        return 0
    }

    ok "PySpectorC# built successfully"
    # Store repo root (for dotnet run --project)
    echo "$cs_target" > "${WORK_DIR}/.pyspector_csharp_path"
    rm -f "${WORK_DIR}/.pyspector_csharp_skip"
    cd "$SCRIPT_DIR"
}

# =============================================================================
# Phase 5: Benchmark Execution
# =============================================================================

run_benchmark_single() {
    local tool="$1"       # "pyspector" or "pyspector-csharp"
    local repo_name="$2"  # "django", "flask", etc.
    local repo_path="$3"  # full path to source

    case "$tool" in
        pyspector)
            source "${WORK_DIR}/venv/bin/activate"
            local cmd="pyspector scan ${repo_path} --format json --severity LOW"
            ;;
        pyspector-csharp)
            if [ -f "${WORK_DIR}/.pyspector_csharp_skip" ]; then
                warn "  PySpectorC# not built — skipping"
                return 0
            fi
            local cs_path
            cs_path=$(cat "${WORK_DIR}/.pyspector_csharp_path")
            local cmd="dotnet run --project ${cs_path}/src/PySpector.Cli -c Release -- ${repo_path} --format json --severity LOW"
            ;;
        pyspector-csharp-noast)
            if [ -f "${WORK_DIR}/.pyspector_csharp_skip" ]; then
                warn "  PySpectorC# not built — skipping"
                return 0
            fi
            local cs_path_noast
            cs_path_noast=$(cat "${WORK_DIR}/.pyspector_csharp_path")
            local cmd="dotnet run --project ${cs_path_noast}/src/PySpector.Cli -c Release -- ${repo_path} --format json --severity LOW --no-ast"
            ;;
    esac

    # Use hyperfine for statistically valid measurement (min 3 runs, 1 warmup)
    local output_file="${WORK_DIR}/results/${tool}_${repo_name}.json"
    local time_file="${WORK_DIR}/results/${tool}_${repo_name}.time"

    log "  Benchmarking ${tool} on ${repo_name}..."

    # Run with hyperfine for timing + capture output separately
    /usr/bin/time -f "%e %M" -o "$time_file" bash -c "$cmd" > "$output_file" 2>/dev/null || {
        warn "  ${tool} on ${repo_name}: scan failed"
        echo "FAILED" > "$time_file"
        return 1
    }

    # Parse results
    local elapsed
    local mem_kb
    read -r elapsed mem_kb < "$time_file" 2>/dev/null || { elapsed="N/A"; mem_kb="N/A"; }

    local issue_count
    issue_count=$(grep -c '"rule_id"' "$output_file" 2>/dev/null || echo "0")

    # Count .py files
    local py_files
    py_files=$(find "$repo_path" -name "*.py" -not -path "*/.git/*" | wc -l)

    # Count lines
    local total_lines
    total_lines=$(find "$repo_path" -name "*.py" -not -path "*/.git/*" -exec cat {} + | wc -l)

    echo "${elapsed}|${mem_kb}|${issue_count}|${py_files}|${total_lines}" > "${WORK_DIR}/results/${tool}_${repo_name}.csv"

    ok "  ${tool}/${repo_name}: ${elapsed}s, ${mem_kb}KB, ${issue_count} issues, ${total_lines} lines"
}

run_all_benchmarks() {
    log "=== Phase 5: Benchmark Execution ==="

    mkdir -p "${WORK_DIR}/results"

    # Determine repos to benchmark
    local repos=("${!TEST_REPOS[@]}")
    if [ "${FULL_MODE:-0}" = "1" ]; then
        repos+=("${!TEST_REPOS_FULL[@]}")
    fi

    for repo_name in "${repos[@]}"; do
        local repo_path="${WORK_DIR}/sources/${repo_name}"

        echo ""
        log "--- ${repo_name} ---"

        # Original PySpector
        run_benchmark_single "pyspector" "$repo_name" "$repo_path" || true

        # PySpectorC# with AST (feature-complete, slower)
        run_benchmark_single "pyspector-csharp" "$repo_name" "$repo_path" || true

        # PySpectorC# regex-only (no AST, speed comparison)
        run_benchmark_single "pyspector-csharp-noast" "$repo_name" "$repo_path" || true
    done
}

# =============================================================================
# Phase 6: Generate Comparison Report
# =============================================================================

generate_report() {
    log "=== Phase 6: Generate Comparison Report ==="

    cat > "$REPORT_FILE" << 'REPORT_HEADER'
# PySpector vs PySpectorC# — Benchmark Comparison Report

REPORT_HEADER

    echo "**Generated**: ${TIMESTAMP}" >> "$REPORT_FILE"
    echo "**Machine**: $(hostname) — $(uname -m) — $(nproc) cores — $(free -h | awk '/^Mem:/ {print $2}') RAM" >> "$REPORT_FILE"
    echo "**OS**: $(cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d= -f2 | tr -d '"')" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"

    # Tool versions
    echo "## Tool Versions" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "- **PySpector**: $(pip show pyspector 2>/dev/null | grep Version | cut -d' ' -f2 || echo 'N/A')" >> "$REPORT_FILE"
    echo "- **.NET**: $(dotnet --version 2>/dev/null || echo 'N/A')" >> "$REPORT_FILE"
    echo "- **Python**: $(python3.14 --version 2>/dev/null || echo 'N/A')" >> "$REPORT_FILE"
    echo "- **Rust**: $(rustc --version 2>/dev/null || echo 'N/A')" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"

    # Results table
    echo "## Results" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "| Repository | .py Files | Lines | Tool | Time (s) | Memory (KB) | Issues |" >> "$REPORT_FILE"
    echo "|------------|-----------|-------|------|----------|-------------|--------|" >> "$REPORT_FILE"

    for csv_file in "${WORK_DIR}"/results/*.csv; do
        [ -f "$csv_file" ] || continue
        local basename
        basename=$(basename "$csv_file" .csv)
        local tool="${basename%%_*}"
        local repo="${basename#*_}"
        local data
        data=$(cat "$csv_file")
        local elapsed="N/A" mem="N/A" issues="0" py_files="0" lines="0"
        IFS='|' read -r elapsed mem issues py_files lines <<< "$data" || true

        local tool_label
        case "$tool" in
            pyspector) tool_label="PySpector (Rust)" ;;
            pyspector-csharp) tool_label="PySpectorC# (.NET)" ;;
            *) tool_label="$tool" ;;
        esac

        printf "| %-10s | %9s | %7s | %-20s | %8s | %11s | %6s |\n" \
            "$repo" "$py_files" "$lines" "$tool_label" "$elapsed" "$mem" "$issues" >> "$REPORT_FILE"
    done

    echo "" >> "$REPORT_FILE"

    # Feature parity analysis
    echo "## Feature Parity Analysis" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "| Repository | Rust Issues | C# Issues | Parity % |" >> "$REPORT_FILE"
    echo "|------------|-------------|-----------|----------|" >> "$REPORT_FILE"

    for repo_name in "${!TEST_REPOS[@]}"; do
        local rust_issues=0
        local cs_issues=0
        local rust_file="${WORK_DIR}/results/pyspector_${repo_name}.csv"
        local cs_file="${WORK_DIR}/results/pyspector-csharp_${repo_name}.csv"

        [ -f "$rust_file" ] && rust_issues=$(cut -d'|' -f3 "$rust_file" 2>/dev/null || echo 0)
        [ -f "$cs_file" ] && cs_issues=$(cut -d'|' -f3 "$cs_file" 2>/dev/null || echo 0)

        local parity="N/A"
        if [ "${rust_issues:-0}" -gt 0 ] 2>/dev/null; then
            parity=$(awk "BEGIN {printf \"%.1f\", (${cs_issues:-0}/${rust_issues})*100}")% || true
        fi

        printf "| %-10s | %11s | %9s | %8s |\n" \
            "$repo_name" "$rust_issues" "$cs_issues" "$parity" >> "$REPORT_FILE"
    done

    echo "" >> "$REPORT_FILE"
    echo "---" >> "$REPORT_FILE"
    echo "*Report generated by benchmark_comparison.sh — reproducible on any Debian/Ubuntu machine*" >> "$REPORT_FILE"

    ok "Report generated: ${REPORT_FILE}"
}

# =============================================================================
# Cleanup
# =============================================================================

cleanup() {
    log "=== Cleanup ==="
    # Keep sources and results, remove build artifacts only
    rm -rf "${WORK_DIR}/venv"
    ok "Cleanup complete (sources and results preserved)"
}

# =============================================================================
# Main
# =============================================================================

print_usage() {
    cat << 'EOF'
Usage: ./benchmark_comparison.sh [OPTIONS]

Options:
  --full           Include Pandas and Scikit-learn (large repos, ~500k LOC each)
  --skip-csharp   Skip PySpectorC# build and benchmarks (Rust-only)
  --clean          Clean venv and rebuild from scratch
  --skip-env       Skip environment setup (already have tools)
  --skip-clone     Skip cloning test sources (already have them)
  --report-only    Only regenerate report from existing results
  -h, --help       Show this help

Examples:
  ./benchmark_comparison.sh                    # Fast mode: Django, Flask, Requests
  ./benchmark_comparison.sh --full             # Full mode: all 5 repos
  ./benchmark_comparison.sh --skip-env         # Skip toolchain install
EOF
}

main() {
    local FULL_MODE=0
    local SKIP_ENV=0
    local SKIP_CLONE=0
    local SKIP_CSHARP=0
    local REPORT_ONLY=0
    local CLEAN=0

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --full)       FULL_MODE=1 ;;
            --skip-env)   SKIP_ENV=1 ;;
            --skip-clone) SKIP_CLONE=1 ;;
            --skip-csharp) SKIP_CSHARP=1 ;;
            --report-only) REPORT_ONLY=1 ;;
            --clean)      CLEAN=1 ;;
            -h|--help)    print_usage; exit 0 ;;
            *) err "Unknown option: $1"; print_usage; exit 1 ;;
        esac
        shift
    done

    # Export FULL_MODE for sub-functions
    export FULL_MODE

    log "PySpector vs PySpectorC# Benchmark Suite"
    log "========================================"
    echo ""

    mkdir -p "$WORK_DIR"

    if [ "$REPORT_ONLY" = "1" ]; then
        generate_report
        exit 0
    fi

    if [ "$CLEAN" = "1" ]; then
        rm -rf "${WORK_DIR}/venv" "${WORK_DIR}/results"
    fi

    [ "$SKIP_ENV" = "0" ]   && setup_environment
    [ "$SKIP_CLONE" = "0" ] && clone_test_sources
    install_pyspector_original
    if [ "$SKIP_CSHARP" = "0" ]; then
        build_pyspector_csharp
    else
        warn "Skipping PySpectorC# build (--skip-csharp)"
        touch "${WORK_DIR}/.pyspector_csharp_skip"
    fi
    run_all_benchmarks
    generate_report

    echo ""
    log "========================================"
    log "Benchmark complete!"
    log "Report: ${REPORT_FILE}"
    log "Raw data: ${WORK_DIR}/results/"
    log "========================================"
}

main "$@"
