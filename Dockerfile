FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY NeoTasks.Domain/NeoTasks.Domain.csproj NeoTasks.Domain/
COPY NeoTasks.Service/NeoTasks.Service.csproj NeoTasks.Service/
COPY NeoTasks.Data/NeoTasks.Data.csproj NeoTasks.Data/
COPY NeoTasks.Api/NeoTasks.Api.csproj NeoTasks.Api/
RUN dotnet restore NeoTasks.Api/NeoTasks.Api.csproj
COPY NeoTasks.Domain/ NeoTasks.Domain/
COPY NeoTasks.Service/ NeoTasks.Service/
COPY NeoTasks.Data/ NeoTasks.Data/
COPY NeoTasks.Api/ NeoTasks.Api/
RUN dotnet publish NeoTasks.Api/NeoTasks.Api.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
RUN mkdir /data && chown app:app /data
COPY --from=build /out .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "NeoTasks.Api.dll"]
