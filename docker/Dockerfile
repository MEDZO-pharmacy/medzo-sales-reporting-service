FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore separately so Docker can reuse the NuGet cache when only source files change.
COPY ["medzo-sales-reporting-service.csproj", "./"]
RUN dotnet restore "medzo-sales-reporting-service.csproj"

COPY . .
RUN dotnet publish "medzo-sales-reporting-service.csproj" \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "medzo-sales-reporting-service.dll"]
