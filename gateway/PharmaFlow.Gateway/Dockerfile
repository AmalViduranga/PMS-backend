FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["pharmaflow-backend/contracts/PharmaFlow.Contracts/PharmaFlow.Contracts.csproj", "pharmaflow-backend/contracts/PharmaFlow.Contracts/"]
COPY ["pharmaflow-backend/gateway/PharmaFlow.Gateway/PharmaFlow.Gateway.csproj", "pharmaflow-backend/gateway/PharmaFlow.Gateway/"]
RUN dotnet restore "pharmaflow-backend/gateway/PharmaFlow.Gateway/PharmaFlow.Gateway.csproj"
COPY . .
WORKDIR "/src/pharmaflow-backend/gateway/PharmaFlow.Gateway"
RUN dotnet build "PharmaFlow.Gateway.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmaFlow.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmaFlow.Gateway.dll"]
