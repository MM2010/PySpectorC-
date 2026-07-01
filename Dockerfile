# ─── Stage 1: Build ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/nightly/sdk:10.0-noble AS build
WORKDIR /src

# Restore dependencies first (layer caching)
COPY Directory.Build.props Directory.Packages.props nuget.config* ./
COPY src/PySpector.Core/PySpector.Core.csproj src/PySpector.Core/
COPY src/PySpector.Reporting/PySpector.Reporting.csproj src/PySpector.Reporting/
COPY src/PySpector.Plugins/PySpector.Plugins.csproj src/PySpector.Plugins/
COPY src/PySpector.RustBridge/PySpector.RustBridge.csproj src/PySpector.RustBridge/
COPY src/PySpector.Web/PySpector.Web.csproj src/PySpector.Web/
COPY src/PySpector.Cli/PySpector.Cli.csproj src/PySpector.Cli/
RUN dotnet restore src/PySpector.Cli/PySpector.Cli.csproj
RUN dotnet restore src/PySpector.Web/PySpector.Web.csproj

# Copy full source and publish
COPY . .
RUN dotnet publish src/PySpector.Cli/PySpector.Cli.csproj \
    -c Release \
    -o /app/cli \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:DebugType=none

RUN dotnet publish src/PySpector.Web/PySpector.Web.csproj \
    -c Release \
    -o /app/web \
    --no-restore \
    -p:PublishSingleFile=true \
    -p:PublishTrimmed=true \
    -p:DebugType=none

# ─── Stage 2: Runtime (CLI tool) ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:10.0-noble-chiseled-extra AS cli
WORKDIR /app
COPY --from=build /app/cli .
COPY rules/built-in-rules.toml rules/
COPY rules/built-in-rules-ai.toml rules/
USER app
ENTRYPOINT ["./pyspector"]

# ─── Stage 3: Runtime (Web API) ───────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/nightly/runtime-deps:10.0-noble-chiseled-extra AS web
WORKDIR /app
COPY --from=build /app/web .
COPY rules/built-in-rules.toml rules/
COPY rules/built-in-rules-ai.toml rules/
USER app
EXPOSE 10000
HEALTHCHECK --interval=30s --timeout=3s CMD curl -f http://localhost:10000/health || exit 1
ENTRYPOINT ["./PySpector.Web"]
