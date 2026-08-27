# syntax=docker/dockerfile:1

# Cloud build: excludes the Windows-only MV Viewer camera SDK.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AsrsWarehouse.csproj", "./"]
RUN dotnet restore "./AsrsWarehouse.csproj" --property:CloudBuild=true

COPY . .
RUN dotnet publish "./AsrsWarehouse.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    --property:CloudBuild=true \
    --property:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "AsrsWarehouse.dll"]
