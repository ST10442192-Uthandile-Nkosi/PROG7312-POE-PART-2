FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Api/SmartX.Api.csproj", "SmartX.Api/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Api/SmartX.Api.csproj"
COPY . .
WORKDIR "/src/SmartX.Api"
RUN dotnet build "SmartX.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "SmartX.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
