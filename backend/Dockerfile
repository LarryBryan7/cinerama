# syntax=docker/dockerfile:1
# Imagen de la API. El contexto de build es ./backend.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo los archivos de proyecto: la restauración de paquetes queda en caché mientras no cambien.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Cinerama.Domain/Cinerama.Domain.csproj src/Cinerama.Domain/
COPY src/Cinerama.Application/Cinerama.Application.csproj src/Cinerama.Application/
COPY src/Cinerama.Infrastructure/Cinerama.Infrastructure.csproj src/Cinerama.Infrastructure/
COPY src/Cinerama.Api/Cinerama.Api.csproj src/Cinerama.Api/
RUN dotnet restore src/Cinerama.Api/Cinerama.Api.csproj

COPY src/ src/
RUN dotnet publish src/Cinerama.Api/Cinerama.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
COPY --from=build /app .

# La imagen trae un usuario sin privilegios (APP_UID): la API no corre como root.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Cinerama.Api.dll"]
