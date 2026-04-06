# ─── Build stage ───
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY LaPlateformeDeLemploiAPI/LaPlateformeDeLemploiAPI.csproj LaPlateformeDeLemploiAPI/
RUN dotnet restore LaPlateformeDeLemploiAPI/LaPlateformeDeLemploiAPI.csproj

COPY LaPlateformeDeLemploiAPI/ LaPlateformeDeLemploiAPI/
WORKDIR /src/LaPlateformeDeLemploiAPI
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Runtime stage ───
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/api/joboffers/stats || exit 1

ENTRYPOINT ["dotnet", "LaPlateformeDeLemploiAPI.dll"]
