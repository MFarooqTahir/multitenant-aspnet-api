FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["multitenant-aspnet-api/multitenant-aspnet-api.csproj", "multitenant-aspnet-api/"]
RUN dotnet restore "multitenant-aspnet-api/multitenant-aspnet-api.csproj"
COPY . .
WORKDIR "/src/multitenant-aspnet-api"
RUN dotnet build "./multitenant-aspnet-api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./multitenant-aspnet-api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "multitenant-aspnet-api.dll"]
