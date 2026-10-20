FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["SmartX.Client/SmartX.Client.csproj", "SmartX.Client/"]
COPY ["SmartX.Shared/SmartX.Shared.csproj", "SmartX.Shared/"]
RUN dotnet restore "SmartX.Client/SmartX.Client.csproj"
COPY . .
RUN dotnet publish "SmartX.Client/SmartX.Client.csproj" -c Release -o /app/publish

FROM nginx:1.27-alpine
COPY SmartX.Client/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
