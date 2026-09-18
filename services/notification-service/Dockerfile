# ============================================================
# Stage 1: BUILD
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

COPY notification-service.csproj ./
RUN dotnet restore --locked-mode

COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ============================================================
# Stage 2: FINAL
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

ARG GIT_SHA=local
ENV GIT_SHA=${GIT_SHA}

WORKDIR /app

RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

COPY --from=build --chown=appuser:appgroup /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "notification-service.dll"]
