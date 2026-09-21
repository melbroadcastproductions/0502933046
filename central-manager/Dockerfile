FROM python:3.11-slim

WORKDIR /app

# Install system dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Install python dependencies
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy application source code
COPY app/ ./app/

EXPOSE 8000

ENV NDI_DISCOVERY_SERVER_HOST=ndi-discovery-server \
    NDI_DISCOVERY_SERVER_PORT=5959

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
