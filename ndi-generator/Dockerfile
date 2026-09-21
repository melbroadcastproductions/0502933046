FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY generator.py .

ENV SERVICE_ID=ndi-gen-01 \
    SERVICE_NAME="NDI Test Pattern Generator" \
    HOST=ndi-generator \
    PORT=8001 \
    CENTRAL_MANAGER_URL=http://central-manager:8000 \
    NDI_DISCOVERY_SERVER_HOST=ndi-discovery-server \
    NDI_DISCOVERY_SERVER_PORT=5959 \
    NDI_STREAM_NAME=NDI_BARS_1080P60 \
    PATTERN=SMPTE_BARS_100 \
    FPS=60 \
    RESOLUTION=1920x1080

CMD ["python3", "-u", "generator.py"]
