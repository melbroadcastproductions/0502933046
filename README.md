# Containerized NDI Services Stack with Central Manager

A production-ready microservices architecture for containerized NDI (Network Device Interface) video streaming in broadcast environments.

---

## 📐 System Architecture

```
                       +-------------------------------+
                       |  NDI Central Manager (8000)   |
                       |  (Registry & Control Plane)   |
                       +---------------+---------------+
                                       ^
                                       |  Heartbeats & REST API
     +---------------------------------+---------------------------------+
     |                                 |                                 |
+----+-------------------+   +---------+-----------+   +-----------------+---+
| NDI Discovery Server   |   | NDI Signal Generator|   | NDI Stream      |
| (Port 5959)            |   | (ndi-generator)     |   | Processor       |
| Stream Lookup Registry |   | Test Pattern Source |   | (ndi-processor) |
+------------------------+   +---------------------+   +-----------------+---+
                                                                 |
                                                       +---------+-----------+
                                                       | NDI Stream          |
                                                       | Health Monitor      |
                                                       | (ndi-monitor)       |
                                                       +---------------------+
```

---

## 🚀 Services Overview

1. **Central Manager (`central-manager/`)**
   - **Port**: `8000`
   - **Role**: Central control plane, web dashboard, and service registry.
   - **Endpoints**:
     - `GET /`: Interactive web dashboard showing stream health and active nodes.
     - `GET /api/v1/services`: List registered NDI services.
     - `POST /api/v1/services/register`: Register a new NDI node.
     - `POST /api/v1/services/{id}/heartbeat`: Ingest heartbeat telemetry and metrics.

2. **NDI Discovery Server (`ndi-discovery-server/`)**
   - **Port**: `5959` (TCP & UDP)
   - **Role**: Centralized NDI directory service allowing cross-subnet stream lookup without mDNS multicast reliance.

3. **NDI Signal Generator (`ndi-generator/`)**
   - **Port**: `8001`
   - **Role**: Containerized broadcast test pattern video generator (SMPTE bars, 1080p60/720p60 profiles).

4. **NDI Stream Processor Gateway (`ndi-processor/`)**
   - **Port**: `8002`
   - **Role**: Transcoding and MJPEG preview feed generation for NDI stream ingest/egress.

5. **NDI Stream Health Monitor (`ndi-monitor/`)**
   - **Port**: `8003`
   - **Role**: Continuous latency, frame drop, and FPS health monitoring for broadcast production feeds.

---

## ⚡ Getting Started

### Prerequisites
- [Docker](https://docs.docker.com/get-docker/)
- [Docker Compose](https://docs.docker.com/compose/) v2+

### Running the Stack

To start the full NDI broadcast container group:

```bash
docker compose up -d --build
```

### Accessing the Dashboard & APIs
- **Central Manager Dashboard**: [http://localhost:8000](http://localhost:8000)
- **Central Manager Health Check**: `curl http://localhost:8000/api/v1/health`
- **Stream Processor Preview**: `http://localhost:8002/preview.mjpeg`
- **Stream Health Monitor API**: `http://localhost:8003/api/v1/metrics`

---

## 🧪 Testing and Verification

Run the integration test suite and stack validator:

```bash
python3 -m unittest discover -s tests
python3 scripts/verify_ndi_stack.py
```
