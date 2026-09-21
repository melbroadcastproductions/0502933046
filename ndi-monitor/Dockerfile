FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY monitor.py .

EXPOSE 8003

ENV SERVICE_ID=ndi-mon-01 \
    SERVICE_NAME="NDI Broadcast Health Monitor" \
    HOST=ndi-monitor \
    PORT=8003 \
    CENTRAL_MANAGER_URL=http://central-manager:8000 \
    NDI_DISCOVERY_SERVER_HOST=ndi-discovery-server \
    NDI_DISCOVERY_SERVER_PORT=5959 \
    MONITORED_STREAMS=NDI_BARS_1080P60,NDI_PROCESSED_720P30

CMD ["python3", "-u", "monitor.py"]
