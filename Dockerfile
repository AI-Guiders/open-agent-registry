FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/OpenAgentRegistry/OpenAgentRegistry.csproj src/OpenAgentRegistry/
RUN dotnet restore src/OpenAgentRegistry/OpenAgentRegistry.csproj
COPY src/OpenAgentRegistry/ src/OpenAgentRegistry/
RUN dotnet publish src/OpenAgentRegistry/OpenAgentRegistry.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8765
ENV OAR_DATABASE_PATH=/data/registry.db
ENV OAR_PUBLIC_BASE_URL=http://127.0.0.1:8765
ENV OAR_DEV_EXPOSE_CLAIM_CODES=false
ENV OAR_DEV_EXPOSE_TOTP_SECRET=false
VOLUME /data
EXPOSE 8765
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OpenAgentRegistry.dll"]
