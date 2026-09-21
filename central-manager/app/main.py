import os
import time
from datetime import datetime, timezone
from typing import Dict, List
from fastapi import FastAPI, Request, HTTPException, status
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from fastapi.responses import HTMLResponse, JSONResponse

from app.models import ServiceRegistration, HeartbeatPayload, ServiceInfo

app = FastAPI(
    title="NDI Broadcast Central Manager",
    description="Central control plane and service registry for containerized NDI broadcast infrastructure.",
    version="1.0.0"
)

# Setup templates and static directories
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
app.mount("/static", StaticFiles(directory=os.path.join(BASE_DIR, "static")), name="static")
templates = Jinja2Templates(directory=os.path.join(BASE_DIR, "templates"))

# In-memory service registry
service_registry: Dict[str, Dict] = {}

# Discovery Server Config
NDI_DISCOVERY_SERVER_HOST = os.getenv("NDI_DISCOVERY_SERVER_HOST", "ndi-discovery-server")
NDI_DISCOVERY_SERVER_PORT = int(os.getenv("NDI_DISCOVERY_SERVER_PORT", "5959"))

@app.get("/", response_class=HTMLResponse)
def get_dashboard(request: Request):
    """Render the central manager web dashboard."""
    now = time.time()
    active_services = []
    for s_id, s_data in list(service_registry.items()):
        # Check if service missed heartbeat (> 30s)
        time_since_hb = now - s_data.get("_last_hb_timestamp", now)
        if time_since_hb > 30:
            s_data["status"] = "stale"
        active_services.append(s_data)

    context = {
        "services": active_services,
        "discovery_host": NDI_DISCOVERY_SERVER_HOST,
        "discovery_port": NDI_DISCOVERY_SERVER_PORT,
        "total_services": len(active_services)
    }

    return templates.TemplateResponse(
        request=request,
        name="index.html",
        context=context
    )

@app.get("/api/v1/health")
def health_check():
    """Health check for Central Manager."""
    return {"status": "ok", "service": "central-manager", "timestamp": datetime.now(timezone.utc).isoformat()}

@app.post("/api/v1/services/register", status_code=status.HTTP_201_CREATED)
def register_service(payload: ServiceRegistration):
    """Register an NDI service with Central Manager."""
    now_iso = datetime.now(timezone.utc).isoformat()
    service_entry = {
        "service_id": payload.service_id,
        "name": payload.name,
        "service_type": payload.service_type,
        "host": payload.host,
        "port": payload.port,
        "ndi_sources": payload.ndi_sources,
        "metadata": payload.metadata,
        "status": "healthy",
        "registered_at": now_iso,
        "last_heartbeat": now_iso,
        "_last_hb_timestamp": time.time(),
        "metrics": {}
    }
    service_registry[payload.service_id] = service_entry
    return {"message": f"Service '{payload.name}' registered successfully", "service": service_entry}

@app.post("/api/v1/services/{service_id}/heartbeat")
def heartbeat(service_id: str, payload: HeartbeatPayload):
    """Receive heartbeat and status metrics from a registered NDI service."""
    if service_id not in service_registry:
        raise HTTPException(status_code=404, detail="Service not registered")

    now_iso = datetime.now(timezone.utc).isoformat()
    s_entry = service_registry[service_id]
    s_entry["status"] = payload.status
    s_entry["metrics"] = payload.metrics
    s_entry["last_heartbeat"] = now_iso
    s_entry["_last_hb_timestamp"] = time.time()
    if payload.active_sources:
        s_entry["ndi_sources"] = payload.active_sources

    return {"message": "Heartbeat received", "service_id": service_id, "status": payload.status}

@app.get("/api/v1/services", response_model=List[ServiceInfo])
def list_services():
    """List all registered NDI broadcast services."""
    now = time.time()
    result = []
    for s_id, s_data in service_registry.items():
        if now - s_data.get("_last_hb_timestamp", now) > 30:
            s_data["status"] = "stale"
        result.append(s_data)
    return result

@app.get("/api/v1/services/{service_id}")
def get_service(service_id: str):
    """Get details of a specific NDI service."""
    if service_id not in service_registry:
        raise HTTPException(status_code=404, detail="Service not found")
    return service_registry[service_id]

@app.delete("/api/v1/services/{service_id}")
def unregister_service(service_id: str):
    """Unregister an NDI service."""
    if service_id not in service_registry:
        raise HTTPException(status_code=404, detail="Service not found")
    removed = service_registry.pop(service_id)
    return {"message": f"Service '{removed['name']}' unregistered successfully"}

@app.get("/api/v1/discovery")
def get_discovery_info():
    """Get configuration for the broadcast NDI Discovery Server."""
    return {
        "discovery_server": f"{NDI_DISCOVERY_SERVER_HOST}:{NDI_DISCOVERY_SERVER_PORT}",
        "host": NDI_DISCOVERY_SERVER_HOST,
        "port": NDI_DISCOVERY_SERVER_PORT,
        "status": "active"
    }
