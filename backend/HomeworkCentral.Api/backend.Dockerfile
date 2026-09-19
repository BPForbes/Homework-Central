# ---------- Build Stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /src

# DOTNET_gcServer=0: the SDK, MSBuild and Roslyn all default to server GC, which sizes its heap
# from the host's core count. On a many-core machine that alone can push a single
# `dotnet publish` past a gigabyte of RSS — and build peak is what decides how much RAM Docker's
# VM has to be given. MSBUILDDISABLENODEREUSE stops MSBuild leaving worker nodes resident after
# the build; DOTNET_EnableDiagnostics=0 drops the diagnostics IPC listener from every process.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_gcServer=0 \
    DOTNET_gcConcurrent=0 \
    DOTNET_EnableDiagnostics=0 \
    MSBUILDDISABLENODEREUSE=1

COPY backend/HomeworkCentral.Api/*.csproj backend/HomeworkCentral.Api/
WORKDIR /src/backend/HomeworkCentral.Api
RUN dotnet restore --disable-parallel

WORKDIR /src
COPY backend/HomeworkCentral.Api backend/HomeworkCentral.Api
COPY frontend/public/favicon.svg frontend/public/favicon.svg
WORKDIR /src/backend/HomeworkCentral.Api
RUN dotnet publish -c Release -o /app/publish --no-restore /p:UseSharedCompilation=false

# ---------- Runtime Stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.9 AS runtime

# Runtime memory tuning. The csproj sets the same GC choices in runtimeconfig.json; these env
# vars are the belt-and-braces copy that also covers `docker run` of this image on its own.
#   DOTNET_gcServer=0 / gcConcurrent=0 — one workstation heap and no background GC thread,
#     instead of a per-core heap plus thread. This is the single largest RSS saving available.
#   DOTNET_GCConserveMemory=5 — return freed segments to the OS instead of holding them.
#   DOTNET_EnableDiagnostics=0 — no profiler or debugger attaches to a compose container.
#   DOTNET_hostBuilder__reloadConfigOnChange=false — stops the host watching every appsettings
#     file (an inotify watch and a thread per file, for config that never changes in an image).
# Compose passes a mem_limit; .NET reads the cgroup limit and derives its heap hard limit from it.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_gcServer=0 \
    DOTNET_gcConcurrent=0 \
    DOTNET_GCConserveMemory=5 \
    DOTNET_EnableDiagnostics=0 \
    DOTNET_hostBuilder__reloadConfigOnChange=false

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

RUN groupadd --gid 1001 appgroup && \
    useradd --uid 1001 --gid 1001 --no-create-home appuser

WORKDIR /app
COPY --from=build /app/publish ./

RUN chown -R appuser:appgroup /app
USER appuser

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# 30s rather than 15s — halves the number of curl processes forked inside the container.
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "HomeworkCentral.Api.dll"]
