FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY NeoTasks.Api/NeoTasks.Api.csproj NeoTasks.Api/
RUN dotnet restore NeoTasks.Api
COPY NeoTasks.Api/ NeoTasks.Api/
RUN dotnet publish NeoTasks.Api -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN mkdir /data && chown app:app /data
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NeoTasks.Api.dll"]
