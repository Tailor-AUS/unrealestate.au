# ═══════════════════════════════════════════════════════════════
# AIGENTS WEB - DOCKERFILE
# ═══════════════════════════════════════════════════════════════

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# wget for HEALTHCHECK — the slim base image ships neither curl nor wget.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files and restore
COPY ["src/Aigents.Web/Aigents.Web.csproj", "src/Aigents.Web/"]
COPY ["src/Aigents.ServiceDefaults/Aigents.ServiceDefaults.csproj", "src/Aigents.ServiceDefaults/"]
RUN dotnet restore "src/Aigents.Web/Aigents.Web.csproj"

# Copy everything and build
COPY . .
WORKDIR "/src/src/Aigents.Web"
RUN dotnet build "Aigents.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Aigents.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Health check — /healthz is the AloomU uptime probe; /health is Aspire's.
# Use a GET (-q -O /dev/null) rather than HEAD (--spider) — .NET's minimal
# /healthz handler accepts GET + HEAD now, but BusyBox-wget --spider on
# older runtimes that only had GET handlers got back 405 → exit 8.
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD wget -q -O /dev/null http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Aigents.Web.dll"]
