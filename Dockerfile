# ---------- Этап 1: сборка ----------
# Берём образ с полным .NET SDK — в нём есть компилятор и dotnet CLI.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# Рабочая директория внутри контейнера, все следующие команды выполняются в ней.
WORKDIR /src

# Сначала копируем только .csproj и восстанавливаем NuGet-пакеты.
# Этот слой кэшируется: пока зависимости не менялись, restore не повторяется.
COPY WarehouseApi.csproj ./
RUN dotnet restore

# Теперь копируем остальной исходный код и публикуем приложение в Release.
COPY . ./
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------- Этап 2: запуск ----------
# Runtime-образ без SDK: меньше размер, меньше поверхность атаки.
FROM mcr.microsoft.com/dotnet/aspnet:10.0

WORKDIR /app

# Забираем из этапа сборки только результат publish (dll + зависимости).
COPY --from=build /app/publish ./

# Kestrel слушает 8080 внутри контейнера; наружу порт пробрасывается через docker run -p.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Команда, которая запускается при старте контейнера.
ENTRYPOINT ["dotnet", "WarehouseApi.dll"]
