# QIAD Helper — single app image (design §13.3). There is no bot image: the Discord bot runs
# inside this ASP.NET Core host (§7.1). Structure copied from QDF's Dockerfile, tags on .NET 10.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
# curl feeds the compose healthcheck against /healthz
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release

# Node is required in the SDK stage: the TypeScript MSBuild compile and the
# `npm run check:views` inline-script guardrail both run during publish (§13.3).
RUN apt-get update \
    && apt-get install -y --no-install-recommends nodejs npm \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# Restore layer: project + central-package files only, so code edits don't bust the cache.
COPY ["global.json", "nuget.config", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["QiadHelper.Core/QiadHelper.Core.csproj", "QiadHelper.Core/"]
COPY ["QiadHelper.Bot/QiadHelper.Bot.csproj", "QiadHelper.Bot/"]
COPY ["QiadHelper.Web/QiadHelper.Web.csproj", "QiadHelper.Web/"]
RUN dotnet restore "./QiadHelper.Web/QiadHelper.Web.csproj"

COPY . .
WORKDIR "/src/QiadHelper.Web"

# The client-script guardrail fails the image build on logic-bearing inline <script> (§12).
RUN npm ci && npm run check:views

RUN dotnet build "./QiadHelper.Web.csproj" -c $BUILD_CONFIGURATION -o /app/build

# ---------------------------------------------------------------------------
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./QiadHelper.Web.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------------------------------------------------------------------------
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "QiadHelper.Web.dll"]
