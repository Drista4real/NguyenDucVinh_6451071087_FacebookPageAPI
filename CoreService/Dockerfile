FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY CoreService.csproj .
RUN dotnet restore CoreService.csproj
COPY . .
RUN dotnet publish CoreService.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 3002
ENTRYPOINT ["dotnet", "CoreService.dll"]
