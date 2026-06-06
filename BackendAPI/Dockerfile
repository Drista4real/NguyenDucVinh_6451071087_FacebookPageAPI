FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY BackendAPI.csproj .
RUN dotnet restore BackendAPI.csproj
COPY . .
RUN dotnet publish BackendAPI.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 3000
ENTRYPOINT ["dotnet", "BackendAPI.dll"]
