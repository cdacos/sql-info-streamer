#!/bin/bash

# Build SQL Info Streamer for multiple platforms using Docker

echo "Building SQL Info Streamer for multiple platforms..."

# Clean up any existing binaries and create build directory
mkdir -p build
rm -f build/sql-info-streamer-*

echo "Building Linux x64 binary..."
docker build --platform linux/amd64 \
    --build-arg DOTNET_RID="linux-x64" \
    -f Dockerfile.build \
    -t sql-builder-linux-x64 .

if [ $? -eq 0 ]; then
    CONTAINER_ID=$(docker create sql-builder-linux-x64)
    docker cp "$CONTAINER_ID:/binaries/sql-info-streamer-linux-x64" "./build/sql-info-streamer-linux-x64"
    docker rm "$CONTAINER_ID" >/dev/null
    chmod +x build/sql-info-streamer-linux-x64
    echo "✅ Linux x64 binary created"
else
    echo "❌ Linux x64 build failed"
fi

echo ""
echo "Building Linux ARM64 binary..."
docker build --platform linux/arm64 \
    --build-arg DOTNET_RID="linux-arm64" \
    -f Dockerfile.build \
    -t sql-builder-linux-arm64 .

if [ $? -eq 0 ]; then
    CONTAINER_ID=$(docker create sql-builder-linux-arm64)
    docker cp "$CONTAINER_ID:/binaries/sql-info-streamer-linux-arm64" "./build/sql-info-streamer-linux-arm64"
    docker rm "$CONTAINER_ID" >/dev/null
    chmod +x build/sql-info-streamer-linux-arm64
    echo "✅ Linux ARM64 binary created"
else
    echo "❌ Linux ARM64 build failed"
fi

echo ""
echo "Building macOS ARM64 binary using local dotnet (if available)..."
if command -v dotnet &> /dev/null && [[ "$OSTYPE" == "darwin"* ]]; then
    dotnet publish SqlInfoStreamer/SqlInfoStreamer.csproj -c Release -r osx-arm64 --self-contained -o ./build-temp
    if [ $? -eq 0 ]; then
        cp ./build-temp/SqlInfoStreamer ./build/sql-info-streamer-macos-arm64
        chmod +x build/sql-info-streamer-macos-arm64
        rm -rf build-temp
        echo "✅ macOS ARM64 binary created"
    else
        echo "❌ macOS ARM64 build failed"
    fi
else
    echo "ℹ️  Skipping macOS build (requires macOS with .NET SDK)"
fi

echo ""
echo "Windows builds require Windows environment. For Windows binary:"
echo "  dotnet publish -c Release -r win-x64 --self-contained"

echo ""
echo "Build complete! Binaries created:"
ls -lh build/sql-info-streamer-* 2>/dev/null || echo "No binaries found"

echo ""
echo "Available binaries:"
[ -f build/sql-info-streamer-linux-x64 ] && echo "  ✅ build/sql-info-streamer-linux-x64      - Linux x64"
[ -f build/sql-info-streamer-linux-arm64 ] && echo "  ✅ build/sql-info-streamer-linux-arm64    - Linux ARM64"
[ -f build/sql-info-streamer-macos-arm64 ] && echo "  ✅ build/sql-info-streamer-macos-arm64    - macOS ARM64"