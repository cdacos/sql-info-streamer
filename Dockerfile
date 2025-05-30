# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

WORKDIR /src
COPY SqlInfoStreamer.csproj .
RUN dotnet restore SqlInfoStreamer.csproj

COPY . .
ARG TARGETPLATFORM
RUN if [ "$TARGETPLATFORM" = "linux/arm64" ]; then \
        RID=linux-arm64; \
    else \
        RID=linux-x64; \
    fi && \
    dotnet publish SqlInfoStreamer.csproj \
    -c Release \
    -r $RID \
    --self-contained \
    -p:PublishTrimmed=false \
    -o /app/publish

# Runtime stage - minimal image with only runtime deps
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0-alpine

# Install SQL Server ODBC drivers for better compatibility
RUN apk add --no-cache \
    unixodbc \
    unixodbc-dev

WORKDIR /app
COPY --from=build /app/publish/ .

# Make executable and verify
RUN chmod +x SqlInfoStreamer && ls -la SqlInfoStreamer

# Set environment variables for proper globalization
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

# Create non-root user for security
RUN adduser -D -s /bin/sh sqlstreamer
USER sqlstreamer

ENTRYPOINT ["./SqlInfoStreamer"]