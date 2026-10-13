FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5005

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["pharmaflow-backend/contracts/PharmaFlow.Contracts/PharmaFlow.Contracts.csproj", "pharmaflow-backend/contracts/PharmaFlow.Contracts/"]
COPY ["pharmaflow-backend/services/documents/PharmaFlow.Documents.Api/PharmaFlow.Documents.Api.csproj", "pharmaflow-backend/services/documents/PharmaFlow.Documents.Api/"]
RUN dotnet restore "pharmaflow-backend/services/documents/PharmaFlow.Documents.Api/PharmaFlow.Documents.Api.csproj"
COPY . .
WORKDIR "/src/pharmaflow-backend/services/documents/PharmaFlow.Documents.Api"
RUN dotnet build "PharmaFlow.Documents.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PharmaFlow.Documents.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PharmaFlow.Documents.Api.dll"]
