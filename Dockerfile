
FROM mcr.microsoft.com/dotnet/aspnet:7.0 AS base
WORKDIR /app

# Build webapp
FROM node:14 AS build_webapp
COPY pgo-web-frontend pgo-web-frontend
WORKDIR pgo-web-frontend
ENV PGO_FRONTEND_BUILD_OUTPUT_DIR=/webapp
RUN npm ci
RUN npm run build -- --no-clean
# When part of the Docker image, the webapp's start page should be in a slightly different place:
RUN mv /webapp/index.html /webapp/index_webapp.html

# Build Sintef.Pgo.REST
FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Sintef.Pgo/REST API/Sintef.Pgo.REST/", "Sintef.Pgo/REST API/Sintef.Pgo.REST/"]
RUN dotnet restore "Sintef.Pgo/REST API/Sintef.Pgo.REST/Sintef.Pgo.REST.csproj"
COPY . .
RUN dotnet build "Sintef.Pgo/REST API/Sintef.Pgo.REST/Sintef.Pgo.REST.csproj" -c Release -o /app/build

# Include webapp
COPY --from=build_webapp ["/webapp", "Sintef.Pgo/REST API/Sintef.Pgo.REST/wwwroot"]

# Publish Sintef.Pgo.REST
FROM build AS publish
WORKDIR /src
RUN dotnet publish "Sintef.Pgo/REST API/Sintef.Pgo.REST/Sintef.Pgo.REST.csproj" -c Release -f net7.0 -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 80
EXPOSE 443
ENTRYPOINT ["dotnet", "Sintef.Pgo.REST.dll"]
