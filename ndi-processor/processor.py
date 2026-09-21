#!/usr/bin/env python3
import os
import time
import socket
import logging
import threading
import requests
from fastapi import FastAPI, Response, HTTPException
import uvicorn

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')

SERVICE_ID = os.getenv("SERVICE_ID", "ndi-proc-01")
SERVICE_NAME = os.getenv("SERVICE_NAME", "NDI Stream Processor Gateway")
HOST = os.getenv("HOST", "ndi-processor")
PORT = int(os.getenv("PORT", "8002"))
CENTRAL_MANAGER_URL = os.getenv("CENTRAL_MANAGER_URL", "http://central-manager:8000")
DISCOVERY_SERVER_HOST = os.getenv("NDI_DISCOVERY_SERVER_HOST", "ndi-discovery-server")
DISCOVERY_SERVER_PORT = int(os.getenv("NDI_DISCOVERY_SERVER_PORT", "5959"))

INPUT_NDI_STREAM = os.getenv("INPUT_NDI_STREAM", "NDI_BARS_1080P60")
OUTPUT_NDI_STREAM = os.getenv("OUTPUT_NDI_STREAM", "NDI_PROCESSED_720P30")

app = FastAPI(title="NDI Stream Processor Gateway API")

# Processing state
state = {
    "status": "processing",
    "ingested_frames": 0,
    "processed_frames": 0,
    "current_fps": 30.0,
    "input_resolution": "1920x1080",
    "output_resolution": "1280x720",
    "transcode_codec": "H.264/AAC"
}

@app.get("/health")
def health():
    return {"status": "ok", "service_id": SERVICE_ID}

@app.get("/preview.mjpeg")
def preview_mjpeg():
    """Generates simulated MJPEG preview stream of processed NDI feed."""
    # Dummy MJPEG header frame for preview gateway
    dummy_frame = b'\xff\xd8\xff\xe0\x00\x10JFIF\x00\x01\x01\x01\x00`\x00`\x00\x00\xff\xdb\x00C\x00' + b'\x00' * 64 + b'\xff\xc0\x00\x0b\x08\x00\x05\x00\x05\x01\x01\x11\x00\xff\xc4\x00\x1f\x00\x00\x01\x05\x01\x01\x01\x01\x01\x01\x00\x00\x00\x00\x00\x00\x00\x00\x01\x02\x03\x04\x05\x06\x07\x08\x09\x0a\x0b\xff\xda\x00\x08\x01\x01\x00\x00?\x00\x7f\x00\xff\xd9'
    return Response(content=dummy_frame, media_type="image/jpeg")

def query_discovery_server():
    """Query available NDI streams from Discovery Server."""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(3.0)
        s.connect((DISCOVERY_SERVER_HOST, DISCOVERY_SERVER_PORT))
        s.sendall(b"QUERY\n")
        resp = s.recv(4096).decode('utf-8')
        s.close()
        logging.info(f"Discovery Server Query Result: {resp.strip()}")
    except Exception as e:
        logging.warning(f"Error querying discovery server: {e}")

def register_with_central_manager():
    """Register processor service with Central Manager."""
    payload = {
        "service_id": SERVICE_ID,
        "name": SERVICE_NAME,
        "service_type": "processor",
        "host": HOST,
        "port": PORT,
        "ndi_sources": [OUTPUT_NDI_STREAM],
        "metadata": {
            "ingests": INPUT_NDI_STREAM,
            "transcode_target": "720p30_mjpeg_preview",
            "codec": state["transcode_codec"]
        }
    }
    url = f"{CENTRAL_MANAGER_URL}/api/v1/services/register"
    try:
        res = requests.post(url, json=payload, timeout=5)
        if res.status_code in (200, 201):
            logging.info("Registered Processor service with Central Manager")
    except Exception as e:
        logging.warning(f"Registration with Central Manager failed: {e}")

def heartbeat_loop():
    """Background thread sending periodic heartbeats and metrics."""
    time.sleep(2)
    register_with_central_manager()

    while True:
        state["ingested_frames"] += 30
        state["processed_frames"] += 30
        payload = {
            "status": "healthy",
            "metrics": {
                "input_stream": INPUT_NDI_STREAM,
                "output_stream": OUTPUT_NDI_STREAM,
                "ingested_frames": state["ingested_frames"],
                "processed_frames": state["processed_frames"],
                "fps": state["current_fps"],
                "input_res": state["input_resolution"],
                "output_res": state["output_resolution"]
            },
            "active_sources": [OUTPUT_NDI_STREAM]
        }
        url = f"{CENTRAL_MANAGER_URL}/api/v1/services/{SERVICE_ID}/heartbeat"
        try:
            requests.post(url, json=payload, timeout=3)
        except Exception:
            pass
        time.sleep(10)

if __name__ == "__main__":
    hb_thread = threading.Thread(target=heartbeat_loop, daemon=True)
    hb_thread.start()

    logging.info(f"Starting NDI Processor Gateway on port {PORT}...")
    uvicorn.run(app, host="0.0.0.0", port=PORT, log_level="info")
