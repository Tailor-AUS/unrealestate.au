# ═══════════════════════════════════════════════════════════════
# AIGENTS API - DOCKERFILE
# ═══════════════════════════════════════════════════════════════

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["src/Aigents.Api/Aigents.Api.csproj", "src/Aigents.Api/"]
COPY ["src/Aigents.Domain/Aigents.Domain.csproj", "src/Aigents.Domain/"]
COPY ["src/Aigents.Infrastructure/Aigents.Infrastructure.csproj", "src/Aigents.Infrastructure/"]
COPY ["src/Aigents.ServiceDefaults/Aigents.ServiceDefaults.csproj", "src/Aigents.ServiceDefaults/"]
RUN dotnet restore "src/Aigents.Api/Aigents.Api.csproj"

# CACHE_BUST changes per commit so the layer cache for COPY . . is
# invalidated and we don't ship stale source. See Web Dockerfile for the
# same fix and the diagnostic that surfaced this.
ARG CACHE_BUST=unset

# Copy everything and build
COPY . .
WORKDIR "/src/src/Aigents.Api"
RUN echo "Cache bust: ${CACHE_BUST}" && dotnet build "Aigents.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Aigents.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Aigents.Api.dll"]
