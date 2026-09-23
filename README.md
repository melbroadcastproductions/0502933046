# NDI Video Dockers Suite with C# .NET Manager

An enterprise-grade broadcast suite built in **C# .NET 10** for deploying, managing, and monitoring scalable groups of **NDI Video Docker** streams with an integrated **ASP.NET Core Manager** and **Quad Split Multiviewer Web Dashboard**.

---

## 🌟 Key Features

- **Multi-Source Stream Generator**:
  - **Color Bars & Test Patterns**: SMPTE bars, Mandelbrot fractals, motion grids.
  - **Show Picture**: Static image & slideshow feeds (local files or remote URLs).
  - **Show Video Clip**: MP4 / MKV / MOV video streams looped seamlessly (local files or remote URLs).
  - **Show Web Page**: Live web pages & local HTML graphics (e.g. live financial stats, stock tickers, dashboards).
- **NDI Discovery Modes**:
  - **Bonjour Mode**: mDNS multicast discovery for local broadcast networks.
  - **Central NDI Server Mode**: Configurable NDI discovery server IP and port (`5959`).
- **Quad Split Multiviewer Monitor Grid**:
  - Live 2x2 broadcast multiviewer layout in Web UI.
- **Tally & UMD Support**:
  - **Tally Lights**: Program (Red), Preview (Green), and Off states with glowing borders.
  - **UMD (Under Monitor Display)**: Stream name, resolution, source mode, and custom labels beneath each screen tile.
- **ASP.NET Core Control Plane**:
  - RESTful API endpoints for lifecycle management (List, Spawn, Start, Stop, Restart, Delete, Logs, Tally).
- **Dual Container & Process Engine**:
  - Native Docker socket integration via `Docker.DotNet` with fallback process runner.

---

## 📁 Repository Architecture

```text
├── NdiWorker/                 # C# .NET 10 NDI Stream Worker Console App
│   ├── Program.cs             # Multi-source generator & Health HTTP server
│   ├── Dockerfile             # Container definition for worker
│   └── NdiWorker.csproj
├── NdiManager/                # C# .NET 10 ASP.NET Core Control Plane Service
│   ├── Controllers/           # REST Controllers (WorkersController, SystemController, TallyController)
│   ├── Services/              # NdiWorkerManager & Docker.DotNet integration
│   ├── Models/                # WorkerConfig, WorkerResponse, SystemStatus models
│   ├── wwwroot/               # Quad Split Web Dashboard UI (index.html, style.css, app.js)
│   ├── Dockerfile             # Container definition for manager
│   └── NdiManager.csproj
├── NdiManager.Tests/          # C# xUnit Test Project
│   └── ManagerTests.cs
├── NdiVideoDockers.slnx       # C# .NET 10 Solution File
├── docker-compose.yml         # Docker orchestration
└── README.md
```

---

## 🚀 Quickstart Guide

### 1. Build and Run via .NET CLI

```bash
# Build the C# .NET 10 solution
dotnet build NdiVideoDockers.slnx

# Run the ASP.NET Core Manager Service
dotnet run --project NdiManager/NdiManager.csproj
```

Open [http://localhost:5000](http://localhost:5000) or [http://localhost:8000](http://localhost:8000) in your web browser.

### 2. Using Docker Compose

```bash
docker-compose up --build
```

---

## 📡 REST API Reference

| Method | Endpoint | Description |
| :--- | :--- | :--- |
| `GET` | `/api/system/status` | Docker engine health & host resource usage |
| `GET` | `/api/workers` | List active and stopped NDI worker streams |
| `POST` | `/api/workers` | Spawn a new NDI video docker instance |
| `GET` | `/api/workers/{id}` | Get worker configuration & live health metrics |
| `POST` | `/api/workers/{id}/start` | Start a stopped NDI worker container |
| `POST` | `/api/workers/{id}/stop` | Stop a running NDI worker container |
| `POST` | `/api/workers/{id}/restart` | Restart an NDI worker container |
| `DELETE` | `/api/workers/{id}` | Terminate and remove an NDI worker container |
| `GET` | `/api/workers/{id}/logs` | Retrieve container stdout/stderr logs |
| `POST` | `/api/tally/{id}` | Update Tally state (`Program`, `Preview`, `Off`) |

---

## 🧪 Testing

Run the xUnit test suite:

```bash
dotnet test NdiManager.Tests/NdiManager.Tests.csproj
```

---

## 📜 License

MIT License.
