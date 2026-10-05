FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY NeoTasks.Domain/NeoTasks.Domain.csproj NeoTasks.Domain/
COPY NeoTasks.Service/NeoTasks.Service.csproj NeoTasks.Service/
COPY NeoTasks.Data/NeoTasks.Data.csproj NeoTasks.Data/
COPY NeoTasks.Worker/NeoTasks.Worker.csproj NeoTasks.Worker/
RUN dotnet restore NeoTasks.Worker/NeoTasks.Worker.csproj
COPY NeoTasks.Domain/ NeoTasks.Domain/
COPY NeoTasks.Service/ NeoTasks.Service/
COPY NeoTasks.Data/ NeoTasks.Data/
COPY NeoTasks.Worker/ NeoTasks.Worker/
RUN dotnet publish NeoTasks.Worker/NeoTasks.Worker.csproj -c Release -o /out --no-restore
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /out .
USER app
ENTRYPOINT ["dotnet", "NeoTasks.Worker.dll"]
