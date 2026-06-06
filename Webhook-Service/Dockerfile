FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Webhook-Service.csproj .
RUN dotnet restore Webhook-Service.csproj
COPY . .
RUN dotnet publish Webhook-Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 3001
ENTRYPOINT ["dotnet", "Webhook-Service.dll"]
