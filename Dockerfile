FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY CashHarbor.Domain/CashHarbor.Domain.csproj CashHarbor.Domain/
COPY CashHarbor.Application/CashHarbor.Application.csproj CashHarbor.Application/
COPY CashHarbor.Infra/CashHarbor.Infra.csproj CashHarbor.Infra/
COPY CashHarbor.client/CashHarbor.client.csproj CashHarbor.client/
RUN dotnet restore CashHarbor.client/CashHarbor.client.csproj
COPY CashHarbor.Domain/ CashHarbor.Domain/
COPY CashHarbor.Application/ CashHarbor.Application/
COPY CashHarbor.Infra/ CashHarbor.Infra/
COPY CashHarbor.client/ CashHarbor.client/
RUN dotnet publish CashHarbor.client/CashHarbor.client.csproj -c Release --no-restore -o /publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN mkdir /data /keys && chown 1654:1654 /data /keys
COPY --from=build /publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__Directory=/data \
    Storage__KeyDirectory=/keys \
    DOTNET_EnableDiagnostics=0 \
    TZ=Europe/Paris
USER 1654:1654
EXPOSE 8080
ENTRYPOINT ["dotnet", "CashHarbor.client.dll"]
