FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Dufo.Credia.slnx && dotnet publish src/Credia.Web/Credia.Web.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN groupadd --system credia && useradd --system --gid credia --home /app credia && mkdir -p /app/keys && chown -R credia:credia /app
COPY --from=build --chown=credia:credia /app/publish .
USER credia
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Credia.Web.dll"]
