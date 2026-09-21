FROM python:3.11-slim

WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY processor.py .

EXPOSE 8002

ENV SERVICE_ID=ndi-proc-01 \
    SERVICE_NAME="NDI Stream Processor Gateway" \
    HOST=ndi-processor \
    PORT=8002 \
    CENTRAL_MANAGER_URL=http://central-manager:8000 \
    NDI_DISCOVERY_SERVER_HOST=ndi-discovery-server \
    NDI_DISCOVERY_SERVER_PORT=5959 \
    INPUT_NDI_STREAM=NDI_BARS_1080P60 \
    OUTPUT_NDI_STREAM=NDI_PROCESSED_720P30

CMD ["python3", "-u", "processor.py"]
