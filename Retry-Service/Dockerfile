FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Retry-Service.csproj .
RUN dotnet restore Retry-Service.csproj
COPY . .
RUN dotnet publish Retry-Service.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 3003
ENTRYPOINT ["dotnet", "Retry-Service.dll"]
