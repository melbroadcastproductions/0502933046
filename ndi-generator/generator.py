#!/usr/bin/env python3
import os
import sys
import time
import socket
import logging
import requests
from datetime import datetime

logging.basicConfig(level=logging.INFO, format='%(asctime)s [%(levelname)s] %(message)s')

# Configuration from environment variables
SERVICE_ID = os.getenv("SERVICE_ID", "ndi-gen-01")
SERVICE_NAME = os.getenv("SERVICE_NAME", "NDI Test Pattern Generator")
HOST = os.getenv("HOST", "ndi-generator")
PORT = int(os.getenv("PORT", "8001"))
CENTRAL_MANAGER_URL = os.getenv("CENTRAL_MANAGER_URL", "http://central-manager:8000")
DISCOVERY_SERVER_HOST = os.getenv("NDI_DISCOVERY_SERVER_HOST", "ndi-discovery-server")
DISCOVERY_SERVER_PORT = int(os.getenv("NDI_DISCOVERY_SERVER_PORT", "5959"))

STREAM_NAME = os.getenv("NDI_STREAM_NAME", "NDI_BARS_1080P60")
PATTERN = os.getenv("PATTERN", "SMPTE_BARS_100")
FPS = int(os.getenv("FPS", "60"))
RESOLUTION = os.getenv("RESOLUTION", "1920x1080")

def register_with_discovery_server():
    """Register NDI stream with NDI Discovery Server on TCP 5959."""
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.settimeout(3.0)
        s.connect((DISCOVERY_SERVER_HOST, DISCOVERY_SERVER_PORT))
        msg = f"REGISTER {STREAM_NAME} {HOST}:{PORT}\n"
        s.sendall(msg.encode('utf-8'))
        resp = s.recv(1024).decode('utf-8')
        logging.info(f"Discovery Server Registration Response: {resp.strip()}")
        s.close()
    except Exception as e:
        logging.warning(f"Could not connect to NDI Discovery Server at {DISCOVERY_SERVER_HOST}:{DISCOVERY_SERVER_PORT}: {e}")

def register_with_central_manager():
    """Register service with Central Manager via REST API."""
    payload = {
        "service_id": SERVICE_ID,
        "name": SERVICE_NAME,
        "service_type": "generator",
        "host": HOST,
        "port": PORT,
        "ndi_sources": [STREAM_NAME],
        "metadata": {
            "pattern": PATTERN,
            "resolution": RESOLUTION,
            "fps": FPS,
            "audio_frequency_hz": 1000
        }
    }
    url = f"{CENTRAL_MANAGER_URL}/api/v1/services/register"
    try:
        res = requests.post(url, json=payload, timeout=5)
        if res.status_code in (200, 201):
            logging.info(f"Successfully registered with Central Manager at {CENTRAL_MANAGER_URL}")
        else:
            logging.warning(f"Failed to register with Central Manager: {res.status_code} - {res.text}")
    except Exception as e:
        logging.warning(f"Error registering with Central Manager at {url}: {e}")

def send_heartbeat(frame_count, start_time):
    """Send operational heartbeat metrics to Central Manager."""
    elapsed = time.time() - start_time
    calculated_fps = round(frame_count / elapsed if elapsed > 0 else FPS, 2)
    payload = {
        "status": "healthy",
        "metrics": {
            "generated_fps": calculated_fps,
            "target_fps": FPS,
            "total_frames": frame_count,
            "resolution": RESOLUTION,
            "pattern": PATTERN,
            "timecode": datetime.utcnow().strftime("%H:%M:%S:%f")[:-3]
        },
        "active_sources": [STREAM_NAME]
    }
    url = f"{CENTRAL_MANAGER_URL}/api/v1/services/{SERVICE_ID}/heartbeat"
    try:
        requests.post(url, json=payload, timeout=3)
    except Exception as e:
        logging.debug(f"Heartbeat send failed: {e}")

def main():
    logging.info(f"Starting NDI Signal Generator '{STREAM_NAME}' ({RESOLUTION}@{FPS}fps)...")

    # Wait briefly for Central Manager and Discovery Server to initialize
    time.sleep(2)
    register_with_discovery_server()
    register_with_central_manager()

    frame_count = 0
    start_time = time.time()
    last_hb_time = 0

    frame_interval = 1.0 / FPS

    while True:
        frame_start = time.time()
        frame_count += 1

        # Periodic heartbeat and re-registration every 10 seconds
        now = time.time()
        if now - last_hb_time >= 10:
            register_with_discovery_server()
            send_heartbeat(frame_count, start_time)
            last_hb_time = now

        # Simulate frame timing loop
        elapsed = time.time() - frame_start
        sleep_time = max(0.001, frame_interval - elapsed)
        time.sleep(sleep_time)

if __name__ == "__main__":
    main()
