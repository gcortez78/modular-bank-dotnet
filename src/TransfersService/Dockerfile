FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/TransfersService/TransfersService.csproj", "src/TransfersService/"]
RUN dotnet restore "src/TransfersService/TransfersService.csproj"

COPY . .
RUN dotnet publish "src/TransfersService/TransfersService.csproj" \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 finbank \
    && useradd --uid 10001 --gid finbank --create-home finbank

WORKDIR /app
COPY --from=build /app/publish .
RUN chown -R finbank:finbank /app /home/finbank

USER 10001
EXPOSE 8080
ENTRYPOINT ["dotnet", "TransfersService.dll"]
