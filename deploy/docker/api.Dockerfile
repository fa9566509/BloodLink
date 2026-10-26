# Multi-stage build producing one image that contains both the API and the
# Worker executables; the worker overrides the entrypoint command
# (docker-compose.yml / Coolify: `dotnet worker/AF.Blood.Worker.dll`).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY AF.Blood.slnx ./
COPY src/AF.Blood.Contracts/AF.Blood.Contracts.csproj src/AF.Blood.Contracts/
COPY src/AF.Blood.Application/AF.Blood.Application.csproj src/AF.Blood.Application/
COPY src/AF.Blood.Infrastructure/AF.Blood.Infrastructure.csproj src/AF.Blood.Infrastructure/
COPY src/AF.Blood.Api/AF.Blood.Api.csproj src/AF.Blood.Api/
COPY src/AF.Blood.Worker/AF.Blood.Worker.csproj src/AF.Blood.Worker/
RUN dotnet restore AF.Blood.slnx

COPY src/ src/
RUN dotnet publish src/AF.Blood.Api/AF.Blood.Api.csproj -c Release -o /app/api --no-restore /p:UseAppHost=false
RUN dotnet publish src/AF.Blood.Worker/AF.Blood.Worker.csproj -c Release -o /app/worker --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/api ./api
COPY --from=build /app/worker ./worker

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Non-root user for defence in depth.
RUN adduser --disabled-password --uid 2000 aff && chown -R aff /app
USER aff

ENTRYPOINT ["dotnet", "api/AF.Blood.Api.dll"]
