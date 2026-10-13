FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5001

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["pharmaflow-backend/contracts/PharmaFlow.Contracts/PharmaFlow.Contracts.csproj", "pharmaflow-backend/contracts/PharmaFlow.Contracts/"]
COPY ["pharmaflow-backend/services/identity/PharmaFlow.Identity.Api/PharmaFlow.Identity.Api.csproj", "pharmaflow-backend/services/identity/PharmaFlow.Identity.Api/"]
RUN dotnet restore "pharmaflow-backend/services/identity/PharmaFlow.Identity.Api/PharmaFlow.Identity.Api.csproj"
COPY . .
WORKDIR "/src/pharmaflow-backend/services/identity/PharmaFlow.Identity.Api"
RUN dotnet build "PharmaFlow.Identity.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmaFlow.Identity.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmaFlow.Identity.Api.dll"]
