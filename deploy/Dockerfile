# syntax=docker/dockerfile:1

# Build stage. The repository pins SDK 10.0.100 with rollForward=latestMajor,
# so an SDK 11 image is used to build the net10.0 target.
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/PrivacyLink.Ip/PrivacyLink.Ip.csproj src/PrivacyLink.Ip/
RUN dotnet restore src/PrivacyLink.Ip/PrivacyLink.Ip.csproj

COPY src/PrivacyLink.Ip/ src/PrivacyLink.Ip/
RUN dotnet publish src/PrivacyLink.Ip/PrivacyLink.Ip.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# Runtime stage. Small ASP.NET Core runtime image, no SDK, non-root process.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl is only used by the container healthcheck.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN groupadd --system --gid 1001 appuser \
    && useradd --system --uid 1001 --gid appuser --create-home appuser

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

EXPOSE 8080
USER appuser

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["curl", "--fail", "--silent", "--show-error", "http://127.0.0.1:8080/healthz"]

ENTRYPOINT ["dotnet", "PrivacyLink.Ip.dll"]
