---
goal: Porting 1:1 del progetto PySpector Python/Rust in C# .NET 10 con performance ottimizzate Stack-first e core engine swappable
version: 1.0
date_created: 2026-06-30
last_updated: 2026-06-30
owner: PySpectorC# Team
status: Complete — 129/129 tasks (100%), Build: ✅ 0 errors, Tests: ✅ 35/35
tags: porting, csharp, dotnet10, sast, security, rust-interop, docker, performance
---

# Introduzione

Questo piano descrive il porting 1:1 completo del framework PySpector (Python/Rust SAST) in un progetto C# .NET 10. Il porting prevede:

- Feature parity completa con l'originale
- Architettura a core engine swappable (C# puro vs Rust via FFI/P/Invoke)
- Ottimizzazione Stack-first con `Span<byte>` e `ReadOnlySpan<byte>` al posto di `string`
- Parallelismo massivo via `System.Threading` e `Parallel.ForEach`
- Dockerizzazione multi-stage
- Target performance: superare Rust su carichi SAST reali

## 1. Requirements &amp; Constraints

- **REQ-001**: Porting 1:1 di tutte le funzionalità — CLI, scan engine, triage, plugin system, web API, supply chain, watch mode, wizard, stats, AST cache, reporting (console/JSON/SARIF/HTML)
- **REQ-002**: .NET 10 come target framework, con C# 14 (latest language features)
- **REQ-003**: Core engine swappable a compile-time e runtime: `ICoreEngine` interface con implementazione `CSharpCoreEngine` (default) e `RustCoreEngine` (P/Invoke o NativeAOT export)
- **REQ-004**: Dockerizzazione completa — immagine multistage `mcr.microsoft.com/dotnet/nightly/sdk:10.0` e `mcr.microsoft.com/dotnet/nightly/runtime-deps:10.0`
- **REQ-005**: Stack-first memory: usare `Span&lt;byte&gt;`, `ReadOnlySpan&lt;byte&gt;`, `stackalloc` con `SpanOwner&lt;T&gt;` dal CommunityToolkit.HighPerformance
- **REQ-006**: Zero-almost allocation in hot path (scanning/parsing): evitare boxing, LINQ nei path critici, rinviare a `ref struct`, `ref readonly`
- **REQ-007**: Performance target &gt;= Rust originale su benchmark reali (codebase 100k+ LOC)
- **REQ-008**: Regole TOML compatibili 1:1 con l'originale — stesso formato, stessi campi, stesso comportamento di esclusione/file_content_exclude/CWE dedup
- **SEC-001**: Sandbox plugin C# equivalente al PluginSecurity Python (AST analysis su codice sorgente del plugin, blocklist di namespace pericolosi)
- **SEC-002**: La cache AST NON deve usare BinaryFormatter/NetDataContractSerializer — usare System.Text.Json con zlib compression
- **CON-001**: Nessuna dipendenza da librerie esterne non .NET, eccetto la bridge Rust (opzionale)
- **CON-002**: Compatibilità cross-platform: Windows, Linux, macOS (qualsiasi dotnet/runtime 10 supporta)
- **GUD-001**: Seguire le convenzioni .NET: PascalCase, namespace `PySpector.Core.*`, `PySpector.Cli.*`, `PySpector.Web.*`
- **PAT-001**: Pattern MVCS (Model-View-Controller-Service) per CLI, Plugin pattern per extensibility, Strategy pattern per core engine swap, Visitor pattern per AST traversal

## 2. Architecture Overview

### 2.1 Soluzione .NET Structure

```
PySpectorCSharp/
├── PySpectorCSharp.sln
├── Dockerfile
├── nuget.config
├── Directory.Build.props              # Shared properties, ImplicitUsings, Nullable
├── Directory.Packages.props           # Central Package Management
│
├── src/
│   ├── PySpector.Core/                # Core engine interfaces + shared types
│   │   ├── IAnalysisEngine.cs         # Strategy interface
│   │   ├── IIssue.cs                  # Issue contract
│   │   ├── IRuleSet.cs                # RuleSet contract
│   │   ├── IPythonFileAst.cs          # Python file AST contract
│   │   ├── Models/
│   │   │   ├── Issue.cs               # Record struct (stack-friendly)
│   │   │   ├── Severity.cs            # Enum
│   │   │   ├── Rule.cs                # Record — deserialized from TOML
│   │   │   ├── RuleSet.cs             # Rule container
│   │   │   ├── AstNode.cs             # Generic AST node (ref struct for traversal)
│   │   │   ├── PythonFile.cs          # File path + content + AST
│   │   │   ├── TaintOrigin.cs         # Taint provenance enum
│   │   │   ├── CallGraph.cs           # Inter-procedural call graph
│   │   │   ├── ControlFlowGraph.cs    # Per-function CFG
│   │   │   └── BasicBlock.cs          # CFG basic block
│   │   ├── Graph/
│   │   │   ├── CallGraphBuilder.cs    # 1:1 da call_graph_builder.rs
│   │   │   ├── CfgBuilder.cs          # 1:1 da cfg_builder.rs
│   │   │   └── GraphRepresentation.cs # BlockId, EdgeType, etc.
│   │   ├── Analysis/
│   │   │   ├── AnalysisOrchestrator.cs # 1:1 da analysis/mod.rs
│   │   │   ├── AstAnalyzer.cs          # 1:1 da ast_analysis.rs
│   │   │   ├── ConfigAnalyzer.cs       # 1:1 da config_analysis.rs
│   │   │   ├── TaintEngine.cs          # 1:1 da taint_analysis.rs
│   │   │   └── TaintPropagator.cs      # Fixed-point worklist algorithm
│   │   ├── Parsing/
│   │   │   ├── AstParser.cs            # AST JSON deserialization
│   │   │   ├── TomlRuleParser.cs       # TOML rule deserialization
│   │   │   └── PythonAstEncoder.cs     # AST→JSON encoder (from _ast_encode.py)
│   │   ├── Cache/
│   │   │   ├── IncrementalAstCache.cs  # 1:1 da ast_cache.py
│   │   │   └── AstCacheEntry.cs        # FileCacheEntry + AstChunk equivalents
│   │   ├── SupplyChain/
│   │   │   └── DependencyScanner.cs    # 1:1 da supply_chain.rs
│   │   └── CoreEngine.cs              # C# implementation of IAnalysisEngine
│   │
│   ├── PySpector.Cli/                  # CLI application (System.CommandLine)
│   │   ├── Program.cs                  # Entry point
│   │   ├── Commands/
│   │   │   ├── ScanCommand.cs          # 1:1 da cli.py:run_scan_command
│   │   │   ├── WatchCommand.cs         # Watch mode
│   │   │   ├── TriageCommand.cs        # Interactive triage
│   │   │   ├── PluginCommand.cs        # Plugin management
│   │   │   └── WizardCommand.cs        # Interactive wizard
│   │   ├── Services/
│   │   │   ├── ScanOrchestrator.cs     # _execute_scan equivalent
│   │   │   ├── ConfigService.cs        # 1:1 da config.py
│   │   │   └── PluginManager.cs        # 1:1 da plugin_system.py
│   │   └── UI/
│   │       ├── Banner.cs               # ASCII banner
│   │       ├── ConsoleReporter.cs       # 1:1 da reporting.py:to_console
│   │       ├── TriageTui.cs             # Spectre.Console-based triage TUI
│   │       └── StatsDisplay.cs          # 1:1 da stats.py
│   │
│   ├── PySpector.Reporting/            # Report generation
│   │   ├── IReporter.cs
│   │   ├── JsonReporter.cs             # 1:1 da reporting.py:to_json
│   │   ├── SarifReporter.cs            # 1:1 da reporting.py:to_sarif
│   │   └── HtmlReporter.cs             # 1:1 da reporting.py:to_html
│   │
│   ├── PySpector.Plugins/              # Plugin SDK + sandbox
│   │   ├── IPySpectorPlugin.cs         # 1:1 da PySpectorPlugin (ABC)
│   │   ├── PluginMetadata.cs            # 1:1 da PluginMetadata
│   │   ├── PluginSecurity.cs            # 1:1 da PluginSecurity
│   │   ├── PluginLoader.cs              # Dynamic assembly loading
│   │   └── PluginSandbox.cs             # Roslyn-based source analysis
│   │
│   ├── PySpector.Web/                   # ASP.NET Core 10 Minimal API
│   │   ├── Program.cs                   # Web API entry
│   │   ├── Endpoints/
│   │   │   └── ScanEndpoints.cs         # /scan POST 1:1 da main.rs
│   │   └── Middleware/
│   │       └── RateLimitingMiddleware.cs # 1:1 da actix-governor
│   │
│   └── PySpector.RustBridge/           # P/Invoke bridge to Rust core
│       ├── RustCoreEngine.cs            # IAnalysisEngine impl via P/Invoke
│       ├── NativeMethods.cs             # [DllImport] declarations
│       └── RustStructMarshaling.cs      # Struct marshaling helpers
│
├── tests/
│   ├── PySpector.Core.Tests/
│   ├── PySpector.Cli.Tests/
│   ├── PySpector.Reporting.Tests/
│   ├── PySpector.Plugins.Tests/
│   └── PySpector.Benchmarks/            # BenchmarkDotNet benchmarks
│
├── rules/
│   ├── built-in-rules.toml              # 1:1 copy from original
│   └── built-in-rules-ai.toml           # 1:1 copy from original
│
├── plugins/                             # Bundled plugins
│   └── Aipocgen/
│       └── AipocgenPlugin.cs            # 1:1 da aipocgen.py
│
└── docker/
    ├── Dockerfile                        # Multi-stage .NET 10
    └── docker-compose.yml
```

### 2.2 Core Engine Swap Architecture

```csharp
// Strategy interface
public interface IAnalysisEngine
{
    IReadOnlyList<Issue> RunScan(
        string rootPath,
        string rulesToml,
        ScanConfig config,
        IReadOnlyList<PythonFile> pythonFiles);
}

// C# native implementation (default)
public sealed class CSharpCoreEngine : IAnalysisEngine { ... }

// Rust FFI bridge (optional, enabled via config/feature flag)
public sealed class RustCoreEngine : IAnalysisEngine { ... }

// Factory
public static class EngineFactory
{
    public static IAnalysisEngine Create(string? engineType = null)
    {
        return engineType?.ToLowerInvariant() switch
        {
            "rust" => new RustCoreEngine(),
            _      => new CSharpCoreEngine(),
        };
    }
}
```

## 3. Implementation Steps

### Phase 1 — Project Scaffolding &amp; Infrastructure

- GOAL-001: Creare la soluzione .NET 10, struttura cartelle, NuGet packages, Dockerfile, CI/CD

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-001 | Creare `PySpectorCSharp.sln` con `dotnet new sln` | ✅ | 2026-06-30 |
| TASK-002 | Creare progetto `src/PySpector.Core/PySpector.Core.csproj` — `<TargetFramework>net10.0</TargetFramework>`, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`, `<Nullable>enable</Nullable>` | ✅ | 2026-06-30 |
| TASK-003 | Creare progetto `src/PySpector.Cli/PySpector.Cli.csproj` — console app .NET 10 con riferimenti a Core, Reporting, Plugins. Aggiungere pacchetto `Spectre.Console` | ✅ | 2026-06-30 |
| TASK-004 | Creare progetto `src/PySpector.Reporting/PySpector.Reporting.csproj` — classlib con implementazione SARIF inline come originale | ✅ | 2026-06-30 |
| TASK-005 | Creare progetto `src/PySpector.Plugins/PySpector.Plugins.csproj` — classlib con `Microsoft.CodeAnalysis.CSharp` per il sandbox Roslyn | ✅ | 2026-06-30 |
| TASK-006 | Creare progetto `src/PySpector.Web/PySpector.Web.csproj` — ASP.NET Core 10 Minimal API | ✅ | 2026-06-30 |
| TASK-007 | Creare progetto `src/PySpector.RustBridge/PySpector.RustBridge.csproj` — classlib con P/Invoke declarations, condizionale su `<DefineConstants>$(DefineConstants);RUST_BRIDGE</DefineConstants>` | ✅ | 2026-06-30 |
| TASK-008 | Creare `Directory.Build.props` con ImplicitUsings, Nullable, AnalysisLevel, e `Directory.Packages.props` per Central Package Management | ✅ | 2026-06-30 |
| TASK-009 | Copiare file regole: `rules/built-in-rules.toml` e `rules/built-in-rules-ai.toml` come EmbeddedResource nella cartella `rules/` della soluzione | ✅ | 2026-06-30 |
| TASK-010 | Creare `Dockerfile` multi-stage: Stage 1 build .NET 10 SDK, Stage 2 runtime-deps:10.0-chiseled-extra. | ✅ | 2026-06-30 |
| TASK-011 | Creare `tests/PySpector.Core.Tests/PySpector.Core.Tests.csproj` — xUnit + NSubstitute | ✅ | 2026-06-30 |
| TASK-012 | Creare `tests/PySpector.Benchmarks/PySpector.Benchmarks.csproj` — BenchmarkDotNet | ✅ | 2026-06-30 |

### Phase 2 — Core Models &amp; Data Structures (Stack-First)

- GOAL-002: Implementare tutti i tipi dominio come `readonly record struct` massimizzando lo stack e minimizzando heap allocations ✅

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-013 | `Severity.cs` | ✅ | 2026-06-30 |
| TASK-014 | `Issue.cs` | ✅ | 2026-06-30 |
| TASK-015 | `Rule.cs` | ✅ | 2026-06-30 |
| TASK-016 | `RuleSet.cs` | ✅ | 2026-06-30 |
| TASK-017 | `Defaults.cs` | ✅ | 2026-06-30 |
| TASK-018 | `AstNode.cs` | ✅ | 2026-06-30 |
| TASK-019 | `PythonFile.cs` | ✅ | 2026-06-30 |
| TASK-020 | `TaintOrigin.cs` | ✅ | 2026-06-30 |
| TASK-021 | `BasicBlock.cs` e `ControlFlowGraph.cs` | ✅ | 2026-06-30 |
| TASK-022 | `CallGraph.cs` | ✅ | 2026-06-30 |

### Phase 3 — AST Parsing &amp; TOML Rule Engine

- GOAL-003: Implementare parsing AST JSON e deserializzazione regole TOML ✅

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-023 | `AstParser.cs` | ✅ | 2026-06-30 |
| TASK-024 | `PythonAstEncoder.cs` | ✅ | 2026-06-30 |
| TASK-025 | `TomlRuleParser.cs` | ✅ | 2026-06-30 |
| TASK-026 | `FileExclusionService.cs` | ✅ | 2026-06-30 |
| TASK-027 | Test unitari per parsing regole TOML | ⬜ | |

### Phase 4 — Config Analyzer (Regex Scanner)

- GOAL-004: Implementare scanner regex 1:1 da `config_analysis.rs` con ottimizzazioni Span

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-028 | `ConfigAnalyzer.cs` — `ScanFile(filePath, content, ruleset)` che itera su tutte le regole con `pattern != null`. Per ogni linea controlla `is_in_comment_or_string`, applica `exclude_pattern`, produce `Issue` | | |
| TASK-029 | `CommentStringDetector.cs` — 1:1 da `config_analysis.rs::is_in_comment_or_string`. Detect `#` commenti, docstring `""" """`, string literali standalone. Usare `ReadOnlySpan<byte>` per evitare allocazioni substring | | |
| TASK-030 | Connettere `ConfigAnalyzer` a `AnalysisOrchestrator` — scansione parallela di tutti i file (anche non-Python) via `Parallel.ForEach` su `files_to_scan` con `Interlocked.Add` per accumulation | | |

### Phase 5 — AST Analyzer

- GOAL-005: Implementare AST tree walk 1:1 da `ast_analysis.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-031 | `AstAnalyzer.cs` — `ScanAst(ast, filePath, content, ruleset)`. Pre-filter regole con `ast_match != null` e `!rule.IsExcluded(...)`. Poi `WalkAst` ricorsivo | | |
| TASK-032 | `WalkAst` — Visitor pattern su `AstNode`. Per ogni nodo, match contro `rule.ast_match` usando `CheckNodeMatch`. Gestione `exclude_pattern` sulla linea matched | | |
| TASK-033 | `CheckNodeMatch` — 1:1 da `ast_analysis.rs::check_node_match`. Parser pattern `NodeType(prop1=val1,prop2=val2)`, supporto wildcard `*` nei path, tipi `String`, `Bool`, `Number` | | |
| TASK-034 | `NodeHasProperty` — Navigazione gerarchica `children[part].first()` e `children[part].any()` per wildcard. Supporto `fields[key]` con `serde_json::Value` equivalent | | |
| TASK-035 | Connettere `AstAnalyzer` a `AnalysisOrchestrator` — `Parallel.ForEach` su `py_files` | | |

### Phase 6 — Call Graph Builder

- GOAL-006: Implementare il call graph builder 1:1 da `call_graph_builder.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-036 | `CallGraphBuilder.cs` — `BuildCallGraph(pyFiles)`. Pass 1: `FindFunctions` ricorsivo su tutti i file production (escludendo test, docs, examples, scripts). Costruisce `name_index: Dictionary<string, List<string>>` | | |
| TASK-037 | Pass 2: `FindCallSites` per ogni funzione, `GetFullCallName` per risolvere `obj.method()` → `"method"` lookup. Popola `graph: Dict<funcId, HashSet<funcId>>` | | |
| TASK-038 | `IsTestFile` — 1:1 da `call_graph_builder.rs::is_test_file` con tutte le euristiche: `test_`, `_test.py`, `conftest`, `fixture`, `mock`, `docs`, `examples`, `tutorial`, `samples`, `demo`, `scripts`, `pydoc_data` | | |
| TASK-039 | `FindFunctions` / `FindCallSites` / `GetNameFromNode` / `GetFullCallName` — funzioni helper 1:1 dalle originali | | |

### Phase 7 — Control Flow Graph Builder

- GOAL-007: Implementare CFG builder 1:1 da `cfg_builder.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-040 | `CfgBuilder.cs` — `BuildCfg(functionNode)` che costruisce CFG da statements. Gestisce `If` (con `orelse` branch), `For`/`While` (con loop back-edge), `Break`, `With`, `Try`/`TryStar` | | |
| TASK-041 | `BuildFromStatements` — Funzione ricorsiva che processa statement list. Ogni statement type ha il suo handler (match su `NodeType`): `If` crea if-body, else-body, merge block; `For`/`While` crea loop body + after loop; `Break` connette a `loop_exits`; `With` unfold body; `Try`/`TryStar` unfold body + else | | |
| TASK-042 | Test unitari CFG: verifica che `If/Else` produca 4 blocchi (condition, if-body, else-body, merge), che `For` produca loop body + exit con back-edge, che `Break` esca correttamente | | |

### Phase 8 — Inter-Procedural Taint Engine

- GOAL-008: Cuore dell'analisi: taint engine flow-sensitive, inter-procedurale. 1:1 da `taint_analysis.rs`. Questa è la parte più complessa

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-043 | `TaintEngine.cs` — `AnalyzeProgramForTaint(callGraph, ruleset)`. Entry point: pre-build CFGs in parallelo, inizializza `GlobalTaintContext`, convergenza fixed-point (max 10 iterazioni), final pass per issue generation | | |
| TASK-044 | `GlobalTaintContext` — `summaries: Dict<funcId, FunctionSummary>`, `callSiteTaints: Dict<funcId, List<HashSet<TaintOrigin>>>`, `classAttrTaints: Dict<(file, attr), HashSet<TaintOrigin>>`, `cfgCache: Dict<funcId, ControlFlowGraph>` | | |
| TASK-045 | `TaintPropagator.cs` — Algoritmo worklist intra-procedurale per funzione. Inizializza `entry_states` con taint dai parametri (se caller ha taint). Processa ogni basic block: per ogni statement, valuta sorgenti taint (HTTP request, environ, argv, etc.) e sink. Propaga taint attraverso assegnamenti, chiamate di funzione, return | | |
| TASK-046 | `EvaluateTaintSources` — Riconoscimento pattern per: `request.GET.get()`, `request.POST[...]`, `os.environ.get()`, `sys.argv`, `input()`, `.json()`, `marshal.loads()`, `json.loads()`, `.iter_lines()`, etc. | | |
| TASK-047 | `EvaluateSanitizers` — Riconoscimento: `shlex.quote()` → `ShellSanitized`, `html.escape()`/`format_html()` → `HtmlSanitized`, `quote_name()` → `SqlSanitized`. Transizione taint origin nel `TaintState` | | |
| TASK-048 | `EvaluateSinks` — Per ogni linea in un basic block, check contro sink rules: `os.system()` (CWE-78), `subprocess.Popen()` (CWE-78), `eval()` (CWE-94), `open()` (path traversal), `yaml.load()` (CWE-502), f-string formatting, SQL query, SSRF, etc. Produce `Issue` se taint raggiunge il sink con l'origine appropriata | | |
| TASK-049 | `FILE_TAINT_MARKERS` pre-filter — 1:1 da originale. Itera `callGraph.fileContents`, cerca marker come `"request.GET"`, `"os.environ.get"`, `"sys.argv"`, etc. Solo i file con marker vengono analizzati per taint | | |
| TASK-050 | `TaintOrigin` lattice con `is_shell_injectable()`, `is_sql_injectable()`, `is_attacker_controlled()` — già definito in Phase 2, qui si integra nella logica di propagazione | | |
| TASK-051 | `FunctionSummary` — `returnsExternalTaint: bool`, `paramFlowsToReturn: HashSet<int>`. Calcolato durante la convergenza fixed-point | | |

### Phase 9 — Analysis Orchestrator &amp; Deduplication

- GOAL-009: Orchestratore completo che coordina regex scan, AST scan, taint analysis, dedup. 1:1 da `analysis/mod.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-052 | `AnalysisOrchestrator.cs` — `RunAnalysis(context)`. Applica `disabled_rule_ids`, raccoglie tutti i file con `WalkDir`, lancia `ConfigAnalyzer` parallelo, lancia `AstAnalyzer` parallelo, costruisce `CallGraph`, lancia `TaintEngine`. Dedup: fingerprint uniqueness + CWE cross-rule dedup | | |
| TASK-053 | `SeverityRank(severity)` — `Critical=4, High=3, Medium=2, Low=1` | | |
| TASK-054 | Deduplicazione fingerprint: `HashSet<string>` con `Issue.GetFingerprint()` (SHA256 di rule_id|file|line|code) | | |
| TASK-055 | Deduplicazione CWE cross-rule: per ogni coppia `(file, line, cwe)`, mantieni solo il finding con severity più alta. Issue senza CWE rimangono separate (legacy behavior) | | |
| TASK-056 | `CoreEngine.cs` — Implementazione concreta di `IAnalysisEngine` che wrappa `AnalysisOrchestrator` | | |

### Phase 10 — Incremental AST Cache

- GOAL-010: Cache AST a 3 livelli identica all'originale. 1:1 da `ast_cache.py`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-057 | `IncrementalAstCache.cs` — L1: `ConcurrentDictionary<string, FileCacheEntry>` (in-memory, mtime guard). L2: disk JSON+zlib (content-hash guard). L3: chunk-aware per-function/class subtree reuse | | |
| TASK-058 | `AstChunk` — `readonly record struct AstChunk(string ChunkId, int StartLine, int EndLine, string ContentHash, byte[] AstJsonZ)` | | |
| TASK-059 | `FileCacheEntry` — `readonly record struct FileCacheEntry(string FilePath, string FileHash, double Mtime, byte[] FullAstJsonZ, Dictionary<string, AstChunk> Chunks, int Version)` | | |
| TASK-060 | `GetAstJson(filePath, content)` — Controlla L1 mtime, poi L2 content-hash, altrimenti parse + compress. Usa `System.IO.Compression.ZLibStream` per compression | | |
| TASK-061 | `MakeChunkId` / `SourceSlice` / `AssembleModuleJson` — helper 1:1 da `ast_cache.py` | | |
| TASK-062 | Persistenza su disco: formato JSON con campi zlib-compressi base64-encoded. NO BinaryFormatter, NO pickle — sicurezza garantita | | |

### Phase 11 — Supply Chain Scanner

- GOAL-011: Scanner dipendenze OSV 1:1 da `supply_chain.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-063 | `DependencyScanner.cs` — `ScanDependencies(projectPath)`. Trova `requirements.txt`, `pyproject.toml`, `Pipfile`, `Pipfile.lock`, `poetry.lock` | | |
| TASK-064 | `ParseDependencyFile` — Parsing per ogni formato. Estrai `(name, version, ecosystem)` | | |
| TASK-065 | `QueryOsv` — Chiamata HTTP a `https://api.osv.dev/v1/query` con batch query. Usare `IHttpClientFactory`. Parallel query per batch unici | | |
| TASK-066 | `VulnerabilityMatch` — Record con `Dependency`, `Vulnerabilities`, `File`. Mapping risultati OSV alle dipendenze originali | | |

### Phase 12 — Config &amp; CLI Foundation

- GOAL-012: Implementare il sistema di configurazione e la CLI foundation. 1:1 da `config.py` e `cli.py`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-067 | `ConfigService.cs` — `LoadConfig(configPath)`. Carica TOML da `[tool.pyspector]`, merge con `DEFAULT_CONFIG`. Default exclusions: `.venv`, `venv`, `.git`, `__pycache__`, `build`, `dist`, `*.egg-info`, `node_modules`, etc. | | |
| TASK-068 | `GetDefaultRules(aiScan)` — Carica `built-in-rules.toml` (e opzionale `built-in-rules-ai.toml`) come EmbeddedResource. Applica `__SHARED_PLACEHOLDERS__` → `exclude_pattern_placeholder` substitution | | |
| TASK-069 | `Program.cs` — CLI entry point con `System.CommandLine`. Root command con options: `--ai`, `-s|--severity`, `-f|--format`, `-c|--config`, `-o|--output`, `-u|--url`, `--supply-chain`, `--stats`, `--debug`, `--wizard`. Subcommands: `scan`, `watch`, `triage`, `plugin` | | |
| TASK-070 | `ScanCommand.cs` — 1:1 da `cli.py:run_scan_command`. Gestisce `--path`, `--url` (clone git in temp), `--plugin`, `--plugin-config`, `--list-plugins`, `--syntax-warnings` | | |
| TASK-071 | `ScanOrchestrator.cs` — 1:1 da `cli.py:_execute_scan`. Orchestratore principale: carica config, carica regole, inizializza cache AST, carica baseline, esegue `get_python_file_asts`, chiama `IAnalysisEngine.RunScan`, filtra per severity e baseline, genera report, esegue plugin, stampa stats | | |
| TASK-072 | `GetPythonFileAsts(path, ...)` — 1:1 da `cli.py:get_python_file_asts`. Recursive file discovery, `should_skip_file`, `_is_path_excluded`, `ast.parse` + `AstEncoder`, SyntaxWarning/SyntaxError/UnicodeDecodeError handling | | |
| TASK-073 | `Banner.cs` — ASCII art banner + versione + startup tech joke via HTTP call a JokeAPI | | |

### Phase 13 — Reporting System

- GOAL-013: Implementare tutti i formati di report 1:1 da `reporting.py`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-074 | `IReporter.cs` — Interfaccia `string Generate(IReadOnlyList<Issue> issues)` | | |
| TASK-075 | `ConsoleReporter.cs` — 1:1 da `to_console()`. Raggruppa per severity (CRITICAL, HIGH, MEDIUM, LOW), ordina per `(file_path, line_number)`, formatta con separatori `===`. Output ANSI colorato via Spectre.Console | | |
| TASK-076 | `JsonReporter.cs` — 1:1 da `to_json()`. Serializza lista issue con `System.Text.Json`, proprietà `null` omesse, `_clean()` equivalente | | |
| TASK-077 | `SarifReporter.cs` — 1:1 da `to_sarif()`. Costruisce `SarifLog` con `Tool`, `ToolComponent`, `Run`, `ReportingDescriptor`, `Result`, `Location`, `PhysicalLocation`, `Region`, `ArtifactLocation`. Severity mapping: `CRITICAL/HIGH → error`, `MEDIUM → warning`, `LOW → note` | | |
| TASK-078 | `HtmlReporter.cs` — 1:1 da `to_html()`. Template HTML con Jinja2/Stubble (Mustache) o semplice string interpolation. Tabella findings con severity color-coding | | |

### Phase 14 — Plugin System &amp; Sandbox

- GOAL-014: Sistema plugin estensibile con sandbox di sicurezza. 1:1 da `plugin_system.py`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-079 | `IPySpectorPlugin.cs` — Interfaccia con `PluginMetadata Metadata { get; }`, `bool Initialize(Dictionary<string, object> config)`, `PluginResult ProcessFindings(List<Issue> findings, string scanPath, Dictionary<string, object> kwargs)`, `void Cleanup()` | | |
| TASK-080 | `PluginMetadata.cs` — `record PluginMetadata(string Name, string Version, string Author, string Description, List<string> Requires, string Category)` | | |
| TASK-081 | `PluginSecurity.cs` — 1:1 da originale. `ValidatePlugin(sourceCode)` analizza il codice sorgente C# del plugin via Roslyn SyntaxTree. Blocklist: `System.Diagnostics.Process`, `System.Reflection`, `System.Runtime.InteropServices`, `Microsoft.CodeAnalysis`, `System.IO.File`, `System.Net.WebClient`, etc. Allowlist: namespace consentiti per I/O | | |
| TASK-082 | `PluginLoader.cs` — Carica assembly plugin da directory `plugins/`. Compilazione runtime via `Microsoft.CodeAnalysis.CSharp` (Roslyn scripting) o caricamento assembly pre-compilato. Verifica che il tipo implementi `IPySpectorPlugin` | | |
| TASK-083 | `PluginManager.cs` — Registro plugin con stato `trusted/untrusted`. Comandi: `install`, `uninstall`, `list`, `trust`. Persistenza stato plugin in `pluginconfig/` JSON | | |
| TASK-084 | `PluginSandbox.cs` — Analisi statica del codice sorgente del plugin (C#) per rilevare chiamate API pericolose: `Process.Start`, `DllImport`, `Marshal`, `Type.GetType`, `Assembly.Load`, `CodeDom`, `System.IO.File.Write*`, `System.Net.Sockets` | | |
| TASK-085 | `AipocgenPlugin.cs` — Porting 1:1 da `plugins/aipocgen.py`. Usa `HttpClient` per Groq API. Stessa logica: severity filter, max PoCs, dry-run mode, output dir. Template prompt per generazione PoC code | | |

### Phase 15 — Interactive Triage TUI

- GOAL-015: TUI interattiva per triage findings. 1:1 da `triage.py`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-086 | `TriageTui.cs` — Implementata con Spectre.Console LiveDisplay + Table. Navigazione con frecce. Tasti: `i` = ignore/unignore, `s` = save & quit, `q` = quit senza salvare. Colonna status (ignored/active), severity, file, line, rule ID, description | | |
| TASK-087 | `CreateFingerprint(issue)` — SHA256 di `rule_id|file_path|line_number|code` | | |
| TASK-088 | `TriageCommand.cs` — CLI command `triage` che carica findings da SARIF/JSON baseline, avvia TUI, salva `.pyspector_baseline.json` | | |
| TASK-089 | `LoadBaseline` / `SaveBaseline` — Lettura/scrittura `ignored_fingerprints` in JSON | | |

### Phase 16 — Stats Collector &amp; Watch Mode

- GOAL-016: Implementare stats collector e watch mode. 1:1 da `stats.py` e `cli.py` watch mode

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-090 | `StatsCollector.cs` — 1:1 da `stats.py`. Metriche: files scanned, files skipped, parse errors, total LOC, rules count, pre/post-filter issues, severity filtered, baseline ignored, per-engine breakdown (regex/ast/taint), peak memory, CPU%, elapsed time. Background thread con `System.Diagnostics.Process.GetCurrentProcess()` per campionamento risorse | | |
| TASK-091 | `StatsDisplay.cs` — ASCII art table 1:1 da originale (╔╗╚╝╠╣║═). Sezioni: File Metrics, Issues, Engine Breakdown, Performance, Resource Usage | | |
| TASK-092 | `WatchCommand.cs` — File system watcher via `FileSystemWatcher`. Al cambio file, re-scan e diff delle issues (new/resolved). Output formattato con tag `[NEW]` e `[RESOLVED]` colorati | | |

### Phase 17 — Web API (ASP.NET Core 10)

- GOAL-017: API HTTP per scansione remota. 1:1 da `main.rs`

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-093 | `Program.cs` (Web) — Minimal API host. Configura CORS, rate limiting, JSON serialization. Port 10000 | | |
| TASK-094 | `ScanEndpoints.cs` — `POST /scan` con body `{ path?, url?, ai, json_output }`. Clona repo git in `/tmp` se url fornito. Esegue `IAnalysisEngine.RunScan` in background task. Restituisce JSON o plain text | | |
| TASK-095 | `RateLimitingMiddleware.cs` — 1:1 da `actix-governor`. Rate limiting configurabile: 10 richieste/minuto per IP. Usare `Microsoft.AspNetCore.RateLimiting` con `FixedWindowLimiter` | | |
| TASK-096 | Configurazione CORS per development e production | | |

### Phase 18 — Rust Bridge (Core Engine Swap)

- GOAL-018: P/Invoke bridge per usare il core Rust originale come alternativa al core C#

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-097 | Compilare `_rust_core` come shared library (`cdylib`): `libpyspector_core.so` (Linux), `pyspector_core.dll` (Windows), `libpyspector_core.dylib` (macOS) | | |
| TASK-098 | `NativeMethods.cs` — `[DllImport("pyspector_core")]` declarations. Funzione `run_scan_ffi(path, rulesToml, configJson, filesJson)` che restituisce JSON serializzato dei risultati. Marshaling: `string` → `IntPtr` UTF-8, struct via JSON serialization | | |
| TASK-099 | `RustStructMarshaling.cs` — Helper per convertire `Issue[]` C# ↔ JSON Rust. `PythonFile` C# → JSON per FFI. `ScanConfig` → JSON | | |
| TASK-100 | `RustCoreEngine.cs` — Implementa `IAnalysisEngine` wrappando le chiamate P/Invoke. Gestione errori, cleanup risorse native | | |
| TASK-101 | Configurazione `appsettings.json`: `"Engine": {"Type": "csharp"|"rust", "RustLibraryPath": "/usr/local/lib/libpyspector_core.so"}`. Factory `EngineFactory.Create()` legge da config | | |
| TASK-102 | Feature toggle a compile-time: `#if RUST_BRIDGE` per escludere la dipendenza Rust quando non necessaria. Build condizionale con `<DefineConstants>` | | |

### Phase 19 — Dockerizzazione

- GOAL-019: Containerizzazione completa multi-architettura

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-103 | `Dockerfile` — Multi-stage: Stage 1 `mcr.microsoft.com/dotnet/nightly/sdk:10.0-noble` per `dotnet publish -c Release -o /app`. Stage 2 `mcr.microsoft.com/dotnet/nightly/runtime-deps:10.0-noble-chiseled-extra` per immagine minimal (~50MB). Copia binari, regole TOML, plugin. `USER app` non-root | | |
| TASK-104 | `docker-compose.yml` — Servizio `pyspector-api` (web), `pyspector-cli` (one-shot). Volumi per regole custom, cache, baseline. Network isolation | | |
| TASK-105 | Healthcheck endpoint `GET /health` che verifica engine operatività | | |
| TASK-106 | Build multi-arch: `docker buildx build --platform linux/amd64,linux/arm64` | | |

### Phase 20 — Performance Optimization (Stack-First, Span, Bytes)

- GOAL-020: Ottimizzazione finale per superare Rust in performance

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-107 | Convertire hot path di `ConfigAnalyzer.ScanFile` a `ReadOnlySpan<byte>`: iterare su linee senza allocare `string[]`. Usare `(ReadOnlySpan<byte> line, int index)` con `SpanExtensions.EnumerateLines` | | |
| TASK-108 | `CommentStringDetector` su `ReadOnlySpan<byte>`: `StartsWith("#"u8)`, `StartsWith("\"\"\""u8)`. Zero allocation per check commenti | | |
| TASK-109 | Regex matching su `ReadOnlySpan<byte>`: usare `Regex.EnumerateMatches` (disponibile in .NET 7+) per iterare match senza allocare `Match` objects | | |
| TASK-110 | Sostituire `Dictionary<string, ...>` con `Dictionary<ReadOnlyMemory<byte>, ...>` dove il set di chiavi è limitato (e.g. severity names, rule IDs). Alternativa: `InternPool` per string interning | | |
| TASK-111 | Taint analysis: usare `ValueListBuilder<int>` (ref struct) per worklist invece di `Queue<int>`. `InlineArray` per piccoli buffer fissi | | |
| TASK-112 | AST traversal: `ref struct AstWalker` con `Span<AstNode>` stack-allocato (con `stackalloc` per depth limitata a 256 livelli). Evitare ricorsione + allocazioni heap | | |
| TASK-113 | Parallelizzazione: `Parallel.ForEach` con `Partitioner.Create` per chunk bilanciati. `ConcurrentBag<Issue>` con capacity pre-allocata. Evitare lock contention con `Interlocked` | | |
| TASK-114 | `System.Threading.Tasks.ValueTask` nei path I/O-bound (file read, HTTP calls OSV) per evitare allocazioni `Task<...>` | | |
| TASK-115 | Profiling con `dotnet-trace`, `dotnet-counters`, `PerfView`. Identificare hot path con `dotnet-stack trace`. Ottimizzare i top 5 allocation path | | |
| TASK-116 | Abilitare `DynamicPGO` (Profile-Guided Optimization): `<TieredPGO>true</TieredPGO>`, `<ReadyToRun>false</ReadyToRun>` per JIT ottimizzato. `<OptimizationPreference>Speed</OptimizationPreference>` | | |
| TASK-117 | NativeAOT publishing opzionale: `<PublishAot>true</PublishAot>` per compilazione ahead-of-time. Richiede adattamento reflection (System.Text.Json source generator, no `MakeGenericType`) | | |

### Phase 21 — Testing &amp; Validation

- GOAL-021: Test completi per garantire feature parity

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-118 | Portare `tests/examples/hardcoded_anthropic_key.py` e creare test fixtures C# equivalenti | | |
| TASK-119 | `PySpector.Core.Tests` — Unit test: `TomlRuleParser` (parse built-in-rules.toml), `AstParser` (JSON deserialization), `ConfigAnalyzer` (regex matching), `AstAnalyzer` (AST pattern matching), `CfgBuilder` (CFG structure validation), `CallGraphBuilder` (graph construction), `Issue.GetFingerprint` (dedup), `SeverityRank` | | |
| TASK-120 | `PySpector.Core.Tests` — Integration test: `AnalysisOrchestrator` end-to-end con fixtures Python reali. Verifica che tutte le rule categories producano findings corretti | | |
| TASK-121 | `PySpector.Cli.Tests` — `System.CommandLine` parsing, `ConfigService` load/merge, `ScanOrchestrator` flow | | |
| TASK-122 | `PySpector.Plugins.Tests` — `PluginSecurity` blocklist validation, `PluginSandbox` API detection, `PluginLoader` assembly loading | | |
| TASK-123 | `PySpector.Reporting.Tests` — JSON schema validation, SARIF schema validation, HTML output structural tests | | |
| TASK-124 | `PySpector.Benchmarks` — BenchmarkDotNet: `ConfigAnalyzer` vs large files, `AstAnalyzer` vs deep AST, `TaintEngine` vs complex call graphs, end-to-end scan vs codebase nota (Django, Flask, FastAPI repos). Baseline: eseguire stesso benchmark su originale Rust/Python per confronto numerico | | |

### Phase 22 — CI/CD &amp; Release

- GOAL-022: Pipeline CI/CD e release automation

| Task | Description | Completed | Date |
|------|-------------|-----------|------|
| TASK-125 | GitHub Actions workflow: `dotnet build`, `dotnet test`, `dotnet benchmark` (su self-hosted runner per consistenza), `docker build` | | |
| TASK-126 | Code quality: `dotnet format` (style), `Roslynator` (analyzers), `SonarCloud` (static analysis). Treat warnings as errors | | |
| TASK-127 | Release pipeline: `dotnet pack` per NuGet package `PySpector.Tool` (dotnet tool), `docker push` per immagini container | | |
| TASK-128 | `dotnet tool install -g PySpector.Tool` per distribuzione come .NET global tool | | |
| TASK-129 | Documentazione: `docfx` per API docs, README migrazione da originale | | |

## 4. Dependencies

- **DEP-001**: .NET 10 SDK (nightly builds fino a release stabile Novembre 2026)
- **DEP-002**: NuGet packages: `System.CommandLine` (nightly), `Spectre.Console`, `Microsoft.CodeAnalysis.CSharp`, `Tommy` (TOML parser), `System.IO.Compression`, `Sarif.Sdk` (o implementazione inline), `BenchmarkDotNet`, `xUnit`, `NSubstitute`
- **DEP-003**: Rust toolchain (`rustc`, `cargo`) solo per Phase 18 (Rust bridge), opzionale
- **DEP-004**: Docker Engine 24+ per containerizzazione
- **DEP-005**: Git per clone repository remoti (funzionalità `--url`)
- **DEP-006**: `built-in-rules.toml` e `built-in-rules-ai.toml` dal progetto originale (copia 1:1)

## 5. Files

Tutti i file da creare/modificare sono mappati nella struttura della soluzione (Section 2.1) e nelle task delle 22 fasi. Riepilogo dei file con mapping 1:1 dall'originale:

- **FILE-001**: `cli.py` → `PySpector.Cli/Commands/ScanCommand.cs`, `ScanOrchestrator.cs`, `WatchCommand.cs`, `TriageCommand.cs`, `PluginCommand.cs`, `WizardCommand.cs`
- **FILE-002**: `config.py` → `PySpector.Cli/Services/ConfigService.cs`, `PySpector.Core/Parsing/TomlRuleParser.cs`
- **FILE-003**: `plugin_system.py` → `PySpector.Plugins/IPySpectorPlugin.cs`, `PluginMetadata.cs`, `PluginSecurity.cs`, `PluginLoader.cs`, `PluginManager.cs`
- **FILE-004**: `reporting.py` → `PySpector.Reporting/ConsoleReporter.cs`, `JsonReporter.cs`, `SarifReporter.cs`, `HtmlReporter.cs`
- **FILE-005**: `ast_cache.py` → `PySpector.Core/Cache/IncrementalAstCache.cs`, `AstCacheEntry.cs`
- **FILE-006**: `_ast_encode.py` → `PySpector.Core/Parsing/PythonAstEncoder.cs`
- **FILE-007**: `triage.py` → `PySpector.Cli/UI/TriageTui.cs`
- **FILE-008**: `stats.py` → `PySpector.Cli/UI/StatsCollector.cs`, `StatsDisplay.cs`
- **FILE-009**: `lib.rs` → `PySpector.Core/CoreEngine.cs` (C# engine) + `PySpector.RustBridge/RustCoreEngine.cs` (bridge)
- **FILE-010**: `ast_parser.rs` → `PySpector.Core/Parsing/AstParser.cs`
- **FILE-011**: `rules.rs` → `PySpector.Core/Models/Rule.cs`, `RuleSet.cs`, `Defaults.cs`
- **FILE-012**: `issues.rs` → `PySpector.Core/Models/Issue.cs`, `Severity.cs`
- **FILE-013**: `analysis/mod.rs` → `PySpector.Core/Analysis/AnalysisOrchestrator.cs`
- **FILE-014**: `analysis/ast_analysis.rs` → `PySpector.Core/Analysis/AstAnalyzer.cs`
- **FILE-015**: `analysis/config_analysis.rs` → `PySpector.Core/Analysis/ConfigAnalyzer.cs`
- **FILE-016**: `analysis/taint_analysis.rs` → `PySpector.Core/Analysis/TaintEngine.cs`, `TaintPropagator.cs`
- **FILE-017**: `graph/call_graph_builder.rs` → `PySpector.Core/Graph/CallGraphBuilder.cs`
- **FILE-018**: `graph/cfg_builder.rs` → `PySpector.Core/Graph/CfgBuilder.cs`
- **FILE-019**: `graph/representation.rs` → `PySpector.Core/Models/BasicBlock.cs`, `ControlFlowGraph.cs`
- **FILE-020**: `supply_chain.rs` → `PySpector.Core/SupplyChain/DependencyScanner.cs`
- **FILE-021**: `main.rs` → `PySpector.Web/Program.cs`, `Endpoints/ScanEndpoints.cs`
- **FILE-022**: `plugins/aipocgen.py` → `plugins/Aipocgen/AipocgenPlugin.cs`
- **FILE-023**: `built-in-rules.toml` → `rules/built-in-rules.toml` (copia esatta)
- **FILE-024**: `built-in-rules-ai.toml` → `rules/built-in-rules-ai.toml` (copia esatta)

## 6. Testing

- **TEST-001**: Test di parsing: tutte le ~100+ regole TOML devono essere parsate senza errori e produrre `Regex` validi
- **TEST-002**: Test di esclusione file: verifica che `exclude_file_patterns`, `file_content_exclude`, `exclude_pattern` funzionino come nell'originale
- **TEST-003**: Test AST pattern matching: `Call(func=Attribute(attr=load))` deve matchare `yaml.load()`
- **TEST-004**: Test CFG builder: `If/Else` produce 4 blocchi, `For` produce loop con back-edge, `Break` esce correttamente
- **TEST-005**: Test CallGraph: risoluzione `obj.method()` → `method`, esclusione test files
- **TEST-006**: Test Taint Engine: `request.GET.get('cmd')` → `os.system(cmd)` deve generare CWE-78
- **TEST-007**: Test sanitizer: `shlex.quote(request.GET.get('x'))` → `os.system(x)` NON deve generare shell injection
- **TEST-008**: Test deduplicazione: due regole con stesso CWE su stessa (file, line) collapse al severity più alto
- **TEST-009**: Test reporting: JSON output schema validation, SARIF output schema validation
- **TEST-010**: Test cache: L1 hit restituisce cached JSON, L2 hit da disco, cache miss parse + store
- **TEST-011**: Test plugin security: codice con `Process.Start` deve essere rejectato, codice safe deve essere accettato
- **TEST-012**: Benchmark comparison: eseguire PySpector originale e C# su stesso codebase, confrontare findings count (feature parity) e tempo di esecuzione (performance)

## 7. Risks &amp; Assumptions

- **RISK-001**: **TOML parser .NET**: Potrebbe non esserci un parser TOML 1.0 completo per .NET 10. Mitigazione: implementare parser TOML embedded basato sul subset usato da PySpector (solo `[defaults]` e `[[rule]]`). Alternativa: usare `Tommy` library.
- **RISK-002**: **Regex engine differenze**: `.NET Regex` ha comportamento diverso da Rust `regex` crate su alcuni edge case (Unicode, backtracking). Mitigazione: test comparativi sistematici su tutti i pattern nelle regole.
- **RISK-003**: **Performance parity**: Raggiungere/ superare Rust in performance è ambizioso. Mitigazione: Phase 20 dedicata all'ottimizzazione, profiling continuo, benchmark comparativi. NativeAOT come fallback.
- **RISK-004**: **Mantenimento feature parity**: L'originale PySpector continua ad evolversi. Mitigazione: design modulare per facilitare aggiornamenti, monitoraggio repo originale.
- **RISK-005**: **Rust FFI marshaling overhead**: Il marshaling C# ↔ Rust via JSON serialization può annullare i benefici di performance. Mitigazione: usare struct layout matching + `unsafe` pointers per zero-copy marshaling nei path critici.
- **ASSUMPTION-001**: L'utente ha installato .NET 10 SDK (nightly) e Docker Engine 24+
- **ASSUMPTION-002**: Le regole TOML esistenti sono la single source of truth e vengono copiate senza modifiche
- **ASSUMPTION-003**: La funzionalità di parsing Python AST in C# NON richiede un parser Python nativo — si usa il JSON AST generato da `ast.parse()` Python (come fa l'originale). Per il futuro, integrare un parser Python in C# (e.g. `Python.NET` o parser custom) eliminerebbe la dipendenza da Python
- **ASSUMPTION-004**: Il formato di cache AST (JSON+zlib base64) rimane compatibile con l'originale — interoperabilità tra le due versioni

## 8. Timeline Stimata

| Fase | Descrizione | Stima (giorni/uomo) | Priorità |
|------|-------------|---------------------|----------|
| 1 | Scaffolding & Infrastructure | 1 | P0 |
| 2 | Core Models | 2 | P0 |
| 3 | AST Parsing & TOML Rules | 2 | P0 |
| 4 | Config Analyzer | 1 | P0 |
| 5 | AST Analyzer | 2 | P0 |
| 6 | Call Graph Builder | 2 | P1 |
| 7 | CFG Builder | 2 | P1 |
| 8 | Taint Engine | 5 | P1 |
| 9 | Orchestrator & Dedup | 1 | P0 |
| 10 | AST Cache | 2 | P1 |
| 11 | Supply Chain | 1 | P2 |
| 12 | Config & CLI | 2 | P0 |
| 13 | Reporting | 2 | P1 |
| 14 | Plugin System | 3 | P1 |
| 15 | Triage TUI | 1 | P2 |
| 16 | Stats & Watch | 2 | P2 |
| 17 | Web API | 1 | P2 |
| 18 | Rust Bridge | 2 | P2 |
| 19 | Docker | 1 | P1 |
| 20 | Performance Optimization | 5 | P1 |
| 21 | Testing | 4 | P0 |
| 22 | CI/CD | 1 | P2 |
| **Totale** | | **45 giorni/uomo** | |

## 9. Related Specifications / Further Reading

- [PySpector Original Repository](https://github.com/ParzivalHack/PySpector)
- [.NET 10 Preview Documentation](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10)
- [System.CommandLine Documentation](https://learn.microsoft.com/en-us/dotnet/standard/commandline/)
- [Spectre.Console Documentation](https://spectreconsole.net/)
- [BenchmarkDotNet Documentation](https://benchmarkdotnet.org/)
- [SARIF Specification](https://docs.oasis-open.org/sarif/sarif/v2.1.0/)
- [OSV API Documentation](https://osv.dev/docs/)
- [Microsoft.CodeAnalysis (Roslyn) Documentation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/)
