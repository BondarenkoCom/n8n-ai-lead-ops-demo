FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY dotnet-api/N8nAiLeadOps.DemoApi.csproj dotnet-api/
RUN dotnet restore dotnet-api/N8nAiLeadOps.DemoApi.csproj
COPY dotnet-api/ dotnet-api/
RUN dotnet publish dotnet-api/N8nAiLeadOps.DemoApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:3001
COPY --from=build /app/publish .
CMD ["dotnet", "N8nAiLeadOps.DemoApi.dll"]
