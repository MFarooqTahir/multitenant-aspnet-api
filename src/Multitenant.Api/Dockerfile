# syntax=docker/dockerfile:1@sha256:572cebbc2b15e6f84c077742952674889cb63cd5e9b80abdffcbe587b6f19183
FROM mcr.microsoft.com/dotnet/aspnet:11.0.0@sha256:ca396ece3f2a2e62c7a03f1d7d710b8bc7e8c9dea5597d091178d9f5bfca13f0 AS base
USER root
RUN mkdir -p /app /home/app/.aspnet/DataProtection-Keys \
    && chown -R $APP_UID:$APP_UID /home/app/.aspnet \
    && chmod 0555 /app
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:11.0.100@sha256:2794448a07118a6eaf00f29ab5d2bf44928517ce5ff86505a63a69230374f599 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only restore inputs first so source edits reuse the NuGet restore layer.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Multitenant.Domain/Multitenant.Domain.csproj src/Multitenant.Domain/packages.lock.json src/Multitenant.Domain/
COPY src/Multitenant.Application/Multitenant.Application.csproj src/Multitenant.Application/packages.lock.json src/Multitenant.Application/
COPY src/Multitenant.Infrastructure/Multitenant.Infrastructure.csproj src/Multitenant.Infrastructure/packages.lock.json src/Multitenant.Infrastructure/
COPY src/Multitenant.Api/Multitenant.Api.csproj src/Multitenant.Api/packages.lock.json src/Multitenant.Api/

RUN dotnet restore "src/Multitenant.Api/Multitenant.Api.csproj" --locked-mode

# Analyzer policy affects compilation, not restore.
COPY .editorconfig ./
COPY src/ src/
WORKDIR /src/src/Multitenant.Api

# Publish performs the single Release compilation; no separate build step.
RUN dotnet publish "./Multitenant.Api.csproj" \
    -c "$BUILD_CONFIGURATION" \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore \
    && chmod -R a-w /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Multitenant.Api.dll"]
