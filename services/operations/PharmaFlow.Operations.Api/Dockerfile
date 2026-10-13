FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["pharmaflow-backend/contracts/PharmaFlow.Contracts/PharmaFlow.Contracts.csproj", "pharmaflow-backend/contracts/PharmaFlow.Contracts/"]
COPY ["pharmaflow-backend/services/operations/PharmaFlow.Operations.Api/PharmaFlow.Operations.Api.csproj", "pharmaflow-backend/services/operations/PharmaFlow.Operations.Api/"]
RUN dotnet restore "pharmaflow-backend/services/operations/PharmaFlow.Operations.Api/PharmaFlow.Operations.Api.csproj"
COPY . .
WORKDIR "/src/pharmaflow-backend/services/operations/PharmaFlow.Operations.Api"
RUN dotnet build "PharmaFlow.Operations.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmaFlow.Operations.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmaFlow.Operations.Api.dll"]
