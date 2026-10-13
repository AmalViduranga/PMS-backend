FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5003

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["pharmaflow-backend/contracts/PharmaFlow.Contracts/PharmaFlow.Contracts.csproj", "pharmaflow-backend/contracts/PharmaFlow.Contracts/"]
COPY ["pharmaflow-backend/services/commerce/PharmaFlow.Commerce.Api/PharmaFlow.Commerce.Api.csproj", "pharmaflow-backend/services/commerce/PharmaFlow.Commerce.Api/"]
RUN dotnet restore "pharmaflow-backend/services/commerce/PharmaFlow.Commerce.Api/PharmaFlow.Commerce.Api.csproj"
COPY . .
WORKDIR "/src/pharmaflow-backend/services/commerce/PharmaFlow.Commerce.Api"
RUN dotnet build "PharmaFlow.Commerce.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmaFlow.Commerce.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmaFlow.Commerce.Api.dll"]
