from pydantic import BaseModel, Field
from typing import List, Optional, Dict, Any
from datetime import datetime

class ServiceRegistration(BaseModel):
    service_id: str = Field(..., description="Unique identifier for the NDI service")
    name: str = Field(..., description="Human-readable service name")
    service_type: str = Field(..., description="Type of service: generator, processor, monitor, discovery, etc.")
    host: str = Field(..., description="IP address or hostname of the service")
    port: int = Field(..., description="Port number")
    ndi_sources: List[str] = Field(default_factory=list, description="NDI source names advertised by this service")
    metadata: Dict[str, Any] = Field(default_factory=dict, description="Additional metadata (e.g. video format, resolution, fps)")

class HeartbeatPayload(BaseModel):
    status: str = Field("healthy", description="Status string: healthy, degraded, un-healthy")
    metrics: Dict[str, Any] = Field(default_factory=dict, description="Operational metrics (cpu, memory, fps, dropped_frames, etc.)")
    active_sources: List[str] = Field(default_factory=list, description="Currently active NDI streams")

class ServiceInfo(BaseModel):
    service_id: str
    name: str
    service_type: str
    host: str
    port: int
    ndi_sources: List[str]
    metadata: Dict[str, Any]
    status: str = "healthy"
    last_heartbeat: str
    registered_at: str
    metrics: Dict[str, Any] = Field(default_factory=dict)
