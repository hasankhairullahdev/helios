# ============================================================
# Stage 1: BUILD
# ============================================================
# Pakai SDK image untuk restore + publish.
# Image ini besar (~600MB) tapi hanya dipakai di stage ini —
# tidak akan masuk ke final image.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build

WORKDIR /src

# Copy csproj dulu dan restore — ini layer terpisah supaya Docker cache
# bisa reuse kalau source code berubah tapi dependencies tidak berubah.
# Tanpa ini, setiap kali ada perubahan kode kecil, semua package di-download ulang.
COPY task-service.csproj ./
RUN dotnet restore --locked-mode

# Copy sisa source code dan publish
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# ============================================================
# Stage 2: FINAL (runtime image)
# ============================================================
# aspnet alpine jauh lebih kecil dari SDK — hanya berisi runtime ASP.NET.
# Ini yang akan jadi image final yang di-push ke registry.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final

# ARG untuk menerima Git SHA dari CI (--build-arg GIT_SHA=abc1234)
# ENV supaya nilai ARG tersedia sebagai environment variable saat runtime.
ARG GIT_SHA=local
ENV GIT_SHA=${GIT_SHA}

WORKDIR /app

# Non-root user — security best practice.
# Kalau container di-compromise, attacker tidak punya root access.
RUN addgroup -S appgroup && adduser -S appuser -G appgroup
USER appuser

# Copy hasil publish dari stage build — hanya binary, tidak ada source code
COPY --from=build --chown=appuser:appgroup /app/publish .

# Port 8080 adalah konvensi untuk ASP.NET Core di container
# (bukan 80/443 karena non-root tidak bisa bind ke port < 1024)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "task-service.dll"]
