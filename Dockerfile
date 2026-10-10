# ==========================================
# Stage 1: Build & Publish
# ==========================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files first to leverage Docker layer caching
COPY ["Pegaxus.sln", "./"]
COPY ["Pegaxus.API/Pegaxus.API.csproj", "Pegaxus.API/"]
COPY ["Pegaxus.Service/Pegaxus.Service.csproj", "Pegaxus.Service/"]
COPY ["Pegaxus.Repository/Pegaxus.Repository.csproj", "Pegaxus.Repository/"]
COPY ["Pegaxus.Service.Test/Pegaxus.Service.Test.csproj", "Pegaxus.Service.Test/"]

# Restore dependencies
RUN dotnet restore "Pegaxus.sln"

# Copy the remaining source files
COPY . .

# Build and publish release binaries
WORKDIR "/src/Pegaxus.API"
RUN dotnet publish "Pegaxus.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ==========================================
# Stage 2: Runtime Environment
# ==========================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final

# Install required libraries for globalization and curl for container health check
RUN apk add --no-cache icu-libs curl

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

WORKDIR /app

# Copy published binaries from build stage
COPY --from=build /app/publish .

# Run as non-root user for security
USER $APP_UID

# Expose HTTP port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl --fail http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Pegaxus.API.dll"]
