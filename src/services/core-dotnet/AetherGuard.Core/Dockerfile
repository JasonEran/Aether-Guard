# Multi-stage build for .NET 8 Web API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file(s) and restore as distinct layers
COPY ["src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj", "src/services/core-dotnet/AetherGuard.Core/"]
COPY ["src/shared/protos", "src/shared/protos"]
RUN dotnet restore "src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj"

# Copy remaining source and publish
COPY ["src/services/core-dotnet/AetherGuard.Core", "src/services/core-dotnet/AetherGuard.Core"]
RUN dotnet publish "src/services/core-dotnet/AetherGuard.Core/AetherGuard.Core.csproj" -c Release -o /app/publish

# Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AetherGuard.Core.dll"]
