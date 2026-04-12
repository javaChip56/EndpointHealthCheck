ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:8.0
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:8.0

FROM ${SDK_IMAGE} AS build
WORKDIR /src

COPY ["src/ApiHealthDashboard/ApiHealthDashboard.csproj", "src/ApiHealthDashboard/"]
RUN dotnet restore "src/ApiHealthDashboard/ApiHealthDashboard.csproj"

COPY . .
RUN dotnet publish "src/ApiHealthDashboard/ApiHealthDashboard.csproj" \
    -c Release \
    --no-restore \
    --self-contained false \
    -o /app/publish

FROM ${RUNTIME_IMAGE} AS final
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "ApiHealthDashboard.dll"]
