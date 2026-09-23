FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY NdiWorker.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install FFmpeg, NDI SDK runtime (libndi.so.5), and avahi-daemon for mDNS/Bonjour discovery
RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    fonts-freefont-ttf \
    fontconfig \
    procps \
    curl \
    ca-certificates \
    avahi-daemon \
    avahi-utils \
    libavahi-client3 \
    && curl -fSL "https://downloads.ndi.tv/SDK/NDISDK_Linux/InstallNDISDK_v5_Linux.tar.gz" -o /tmp/ndi.tar.gz \
    && tar -xzf /tmp/ndi.tar.gz -C /tmp \
    && yes y | /tmp/InstallNDISDK_v5_Linux.sh \
    && cp /app/NDI\ SDK\ for\ Linux/lib/x86_64-linux-gnu/libndi.so.5* /usr/lib/ 2>/dev/null || true \
    && rm -rf /tmp/* /app/NDI* \
    && ldconfig \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV STREAM_NAME="NDI-Worker-01" \
    RESOLUTION="1920x1080" \
    FPS="30" \
    PATTERN="smptebars" \
    AUDIO_FREQ="1000" \
    OVERLAY_TEXT="LIVE NDI FEED" \
    DEST_IP="239.255.0.1" \
    DEST_PORT="5004" \
    HEALTH_PORT="8080"

EXPOSE 8080/tcp 5004/udp

ENTRYPOINT ["dotnet", "NdiWorker.dll"]
