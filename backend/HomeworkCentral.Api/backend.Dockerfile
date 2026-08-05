# ---------- Build Stage ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /src

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    DOTNET_NOLOGO=1

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

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

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

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "HomeworkCentral.Api.dll"]
