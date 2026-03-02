FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

COPY CleitinRastaDicordBot.sln .
COPY src/App/App.csproj src/App/
RUN dotnet restore src/App/App.csproj

COPY src/ src/
RUN dotnet publish src/App/App.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "App.dll"]
