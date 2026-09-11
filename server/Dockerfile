# Multi-stage збірка бекенда ASP.NET Core 10.
# Контекст збірки — папка server/.

# ── Етап build: повний SDK, restore + publish ──────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Спершу лише csproj — щоб шар restore кешувався, поки не змінюються залежності.
COPY Beauty.Server.csproj ./
RUN dotnet restore

# Далі решта коду і публікація в Release.
COPY . ./
RUN dotnet publish Beauty.Server.csproj -c Release -o /app --no-restore

# ── Етап runtime: легкий aspnet-образ без SDK ──────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Слухаємо на 8080 всередині мережі docker (публічно не публікуємо — вхід через nginx).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app ./

ENTRYPOINT ["dotnet", "Beauty.Server.dll"]
