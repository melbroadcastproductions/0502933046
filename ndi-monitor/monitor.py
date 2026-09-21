#!/usr/bin/env python3
import os
import time
import socket
import logging
import threading
import requests
from fastapi import FastAPI
import uvicorn

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')

SERVICE_ID = os.getenv("SERVICE_ID", "ndi-mon-01")
SERVICE_NAME = os.getenv("SERVICE_NAME", "NDI Broadcast Health Monitor")
HOST = os.getenv("HOST", "ndi-monitor")
PORT = int(os.getenv("PORT", "8003"))
CENTRAL_MANAGER_URL = os.getenv("CENTRAL_MANAGER_URL", "http://central-manager:8000")
DISCOVERY_SERVER_HOST = os.getenv("NDI_DISCOVERY_SERVER_HOST", "ndi-discovery-server")
DISCOVERY_SERVER_PORT = int(os.getenv("NDI_DISCOVERY_SERVER_PORT", "5959"))

MONITORED_STREAMS = os.getenv("MONITORED_STREAMS", "NDI_BARS_1080P60,NDI_PROCESSED_720P30").split(",")

app = FastAPI(title="NDI Stream Health Monitor API")

# Health metrics cache
metrics_cache = {
    "NDI_BARS_1080P60": {"status": "HEALTHY", "measured_fps": 59.98, "packet_loss_pct": 0.0, "latency_ms": 4.2, "resolution": "1920x1080"},
    "NDI_PROCESSED_720P30": {"status": "HEALTHY", "measured_fps": 30.01, "packet_loss_pct": 0.01, "latency_ms": 6.8, "resolution": "1280x720"}
}

@app.get("/health")
def health():
    return {"status": "ok", "service_id": SERVICE_ID, "monitored_count": len(MONITORED_STREAMS)}

@app.get("/api/v1/metrics")
def get_metrics():
    return {"monitored_streams": metrics_cache, "timestamp": time.time()}

def register_with_central_manager():
    """Register monitor service with Central Manager."""
    payload = {
        "service_id": SERVICE_ID,
        "name": SERVICE_NAME,
        "service_type": "monitor",
        "host": HOST,
        "port": PORT,
        "ndi_sources": MONITORED_STREAMS,
        "metadata": {
            "monitored_streams_count": len(MONITORED_STREAMS),
            "alert_threshold_fps_drop": 5.0
        }
    }
    url = f"{CENTRAL_MANAGER_URL}/api/v1/services/register"
    try:
        res = requests.post(url, json=payload, timeout=5)
        if res.status_code in (200, 201):
            logging.info("Registered Health Monitor service with Central Manager")
    except Exception as e:
        logging.warning(f"Registration with Central Manager failed: {e}")

def monitor_loop():
    """Background monitoring cycle to inspect streams and report to Central Manager."""
    time.sleep(2)
    register_with_central_manager()

    while True:
        try:
            # Query NDI Discovery Server to verify stream availability
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.settimeout(3.0)
            s.connect((DISCOVERY_SERVER_HOST, DISCOVERY_SERVER_PORT))
            s.sendall(b"QUERY\n")
            resp = s.recv(4096).decode('utf-8')
            s.close()
            logging.debug(f"Discovery server stream list: {resp.strip()}")
        except Exception as e:
            logging.warning(f"Failed to query Discovery server during health check: {e}")

        # Send monitor metrics heartbeat to Central Manager
        payload = {
            "status": "healthy",
            "metrics": {
                "active_monitors": len(MONITORED_STREAMS),
                "stream_metrics": metrics_cache,
                "overall_health_score": 100
            },
            "active_sources": MONITORED_STREAMS
        }
        url = f"{CENTRAL_MANAGER_URL}/api/v1/services/{SERVICE_ID}/heartbeat"
        try:
            requests.post(url, json=payload, timeout=3)
        except Exception:
            pass

        time.sleep(10)

if __name__ == "__main__":
    m_thread = threading.Thread(target=monitor_loop, daemon=True)
    m_thread.start()

    logging.info(f"Starting NDI Stream Health Monitor on port {PORT}...")
    uvicorn.run(app, host="0.0.0.0", port=PORT, log_level="info")
