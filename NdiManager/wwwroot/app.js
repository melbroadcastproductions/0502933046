document.addEventListener("DOMContentLoaded", () => {
    // UI Elements
    const engineModeBadge = document.getElementById("engineModeBadge");
    const discoveryModeVal = document.getElementById("discoveryModeVal");
    const workerCountVal = document.getElementById("workerCountVal");
    const cpuUsageVal = document.getElementById("cpuUsageVal");
    const ramUsageVal = document.getElementById("ramUsageVal");
    const quadGrid = document.getElementById("quadGrid");
    const workerGrid = document.getElementById("workerGrid");
    const refreshBtn = document.getElementById("refreshBtn");

    // Modal elements
    const spawnModal = document.getElementById("spawnModal");
    const openModalBtn = document.getElementById("openModalBtn");
    const closeModalBtn = document.getElementById("closeModalBtn");
    const cancelModalBtn = document.getElementById("cancelModalBtn");
    const spawnForm = document.getElementById("spawnForm");

    // Logs elements
    const logsModal = document.getElementById("logsModal");
    const closeLogsBtn = document.getElementById("closeLogsBtn");
    const dismissLogsBtn = document.getElementById("dismissLogsBtn");
    const refreshLogsBtn = document.getElementById("refreshLogsBtn");
    const logsWorkerTitle = document.getElementById("logsWorkerTitle");
    const logsConsole = document.getElementById("logsConsole");

    let currentLogWorkerId = null;

    async function fetchMetrics() {
        try {
            const res = await fetch("/api/system/status");
            if (res.ok) {
                const data = await res.json();
                engineModeBadge.textContent = data.docker_available ? "Docker Engine" : "Process Fallback";
                engineModeBadge.className = `badge ${data.docker_available ? 'badge-green' : 'badge-yellow'}`;
                discoveryModeVal.textContent = "Central Server (127.0.0.1:5959)";
                workerCountVal.textContent = `${data.running_workers} / ${data.total_workers}`;
                cpuUsageVal.textContent = `${data.cpu_usage_percent}%`;
                ramUsageVal.textContent = `${data.memory_usage_mb} MB`;
            }
        } catch (err) {
            console.error(err);
        }
    }

    async function fetchWorkers() {
        try {
            const res = await fetch("/api/workers");
            if (res.ok) {
                const workers = await res.json();
                renderQuadGrid(workers);
                renderWorkerGrid(workers);
            }
        } catch (err) {
            console.error(err);
        }
    }

    function renderQuadGrid(workers) {
        quadGrid.innerHTML = "";

        // Build list of all available stream sources for the selector dropdown
        const availableSourcesOptions = [
            `<option value="">-- Select Input Source --</option>`,
            `<optgroup label="Active Worker NDI Streams">`,
            ...workers.map(w => `<option value="${escapeHtml(w.config.stream_name)}">${escapeHtml(w.config.stream_name)} [${w.config.source_type}]</option>`),
            `</optgroup>`,
            `<optgroup label="Test Patterns & Generator Feeds">`,
            `<option value="smptebars">Color Bars (SMPTE)</option>`,
            `<option value="testsrc2">Color Bars (Test Pattern 2)</option>`,
            `<option value="rgbtestsrc">RGB Test Source</option>`,
            `<option value="mandelbrot">Mandelbrot Motion Pattern</option>`,
            `</optgroup>`
        ].join("");

        for (let i = 0; i < 4; i++) {
            const w = workers[i];
            const tallyClass = w && w.config.tally_state === "Program" ? "tally-program"
                             : w && w.config.tally_state === "Preview" ? "tally-preview" : "";

            const tallyBadgeClass = w && w.config.tally_state === "Program" ? "tally-badge-program"
                                  : w && w.config.tally_state === "Preview" ? "tally-badge-preview" : "tally-badge-off";

            const currentStreamName = w ? w.config.stream_name : `CAM ${i + 1}`;
            const currentSubText = w ? `udp://${w.config.dest_ip}:${w.config.dest_port}` : "NO FEED";

            quadGrid.innerHTML += `
                <div class="quad-tile ${w ? tallyClass : 'empty'}">
                    <div class="quad-screen">
                        <div style="display:flex; align-items:center; justify-content:space-between; width:100%; padding:0 0.5rem; margin-bottom:0.5rem;">
                            <span style="font-weight:700; color:#40a9ff; font-size:0.9rem;"><i class="fa-solid fa-tv"></i> QUAD ${i + 1} INPUT</span>
                            <select class="quad-select" style="background:#1f1f1f; color:#fff; border:1px solid #434343; border-radius:4px; padding:2px 6px; font-size:0.8rem; max-width:200px;" onchange="assignQuadSource(${i}, this.value)">
                                ${availableSourcesOptions}
                            </select>
                        </div>
                        <i class="fa-solid fa-broadcast-tower"></i>
                        <span style="font-weight:700; color:#fff;">${escapeHtml(currentStreamName)}</span>
                        <span>${w ? `${w.config.source_type} | ${w.config.resolution}` : 'Select input source above'}</span>
                        ${w ? `
                        <div class="tally-controls">
                            <button class="tally-btn ${w.config.tally_state === 'Program' ? 'active-pgm' : ''}" onclick="setTally('${w.id}', 'Program')">PGM</button>
                            <button class="tally-btn ${w.config.tally_state === 'Preview' ? 'active-pvw' : ''}" onclick="setTally('${w.id}', 'Preview')">PVW</button>
                            <button class="tally-btn" onclick="setTally('${w.id}', 'Off')">OFF</button>
                        </div>` : ''}
                    </div>
                    <div class="umd-bar">
                        <span class="umd-label">${escapeHtml(w ? (w.config.umd_text || w.config.stream_name) : `CAM ${i + 1}`)}</span>
                        <span class="tally-badge ${tallyBadgeClass}">${w ? w.config.tally_state : 'OFF'}</span>
                        <span class="umd-sub">${currentSubText}</span>
                    </div>
                </div>
            `;
        }
    }

    window.assignQuadSource = async (quadIndex, selectedSource) => {
        if (!selectedSource) return;
        // Search if a QuadSplit worker already exists, or create/update QuadSplit worker with input selection
        const res = await fetch("/api/workers");
        if (res.ok) {
            const workers = await res.json();
            let quadWorker = workers.find(w => w.config.source_type === "QuadSplit");
            let currentSources = quadWorker && quadWorker.config.source_uri ? quadWorker.config.source_uri.split(',') : ["smptebars", "testsrc2", "rgbtestsrc", "mandelbrot"];

            currentSources[quadIndex] = selectedSource;
            const updatedSourcesUri = currentSources.join(',');

            if (quadWorker) {
                // Restart Quad worker with updated inputs
                await fetch(`/api/workers/${quadWorker.id}`, { method: "DELETE" });
            }

            await fetch("/api/workers", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    stream_name: "NDI-QUAD-MULTIVIEWER-01",
                    source_type: "QuadSplit",
                    source_uri: updatedSourcesUri,
                    discovery_mode: "CentralServer",
                    discovery_server_ip: "127.0.0.1",
                    discovery_server_port: 5959,
                    resolution: "1920x1080",
                    fps: "30",
                    tally_state: "Program",
                    umd_text: `QUAD MULTIVIEWER`,
                    audio_freq: 1000,
                    overlay_text: "LIVE 4-WAY MULTIVIEWER FEED",
                    dest_ip: "239.255.0.1",
                    dest_port: 5009,
                    health_port: 8089
                })
            });

            fetchWorkers();
            fetchMetrics();
        }
    };

    function renderWorkerGrid(workers) {
        if (!workers || workers.length === 0) {
            workerGrid.innerHTML = `
                <div style="grid-column: 1 / -1; text-align: center; color: var(--text-secondary); padding: 3rem;">
                    <i class="fa-solid fa-video-slash" style="font-size: 2.5rem; margin-bottom: 1rem;"></i>
                    <p>No NDI video workers running. Click <strong>"Spawn NDI Worker"</strong> above to launch one.</p>
                </div>
            `;
            return;
        }

        workerGrid.innerHTML = workers.map(w => {
            const isRunning = w.status === "running";
            const statusBadge = isRunning
                ? `<span class="badge badge-green"><i class="fa-solid fa-circle"></i> RUNNING</span>`
                : `<span class="badge badge-red"><i class="fa-solid fa-stop"></i> STOPPED</span>`;

            const webrtcBadge = w.config.webrtc_url
                ? `<span class="badge badge-purple" style="background:#722ed1; color:#fff;"><i class="fa-solid fa-bolt"></i> WebRTC</span>`
                : "";

            return `
                <div class="worker-card" data-id="${w.id}">
                    <div class="card-header">
                        <span class="stream-title"><i class="fa-solid fa-broadcast-tower"></i> ${escapeHtml(w.config.stream_name)} ${webrtcBadge}</span>
                        ${statusBadge}
                    </div>
                    <div class="card-body">
                        <div class="info-row"><span>Source Mode:</span> <span class="val">${w.config.source_type}</span></div>
                        <div class="info-row"><span>Source Path/URI:</span> <span class="val">${w.config.source_uri || '(None)'}</span></div>
                        <div class="info-row"><span>Discovery:</span> <span class="val">${w.config.discovery_mode}</span></div>
                        <div class="info-row"><span>Resolution:</span> <span class="val">${w.config.resolution} @ ${w.config.fps} fps</span></div>
                        <div class="info-row"><span>Tally / UMD:</span> <span class="val">${w.config.tally_state} (${w.config.umd_text})</span></div>
                        <div class="info-row"><span>Destination:</span> <span class="val">udp://${w.config.dest_ip}:${w.config.dest_port}</span></div>
                    </div>
                    <div class="card-actions">
                        ${isRunning
                            ? `<button class="btn btn-secondary btn-sm" onclick="stopWorker('${w.id}')"><i class="fa-solid fa-pause"></i> Stop</button>`
                            : `<button class="btn btn-primary btn-sm" onclick="startWorker('${w.id}')"><i class="fa-solid fa-play"></i> Start</button>`
                        }
                        <button class="btn btn-secondary btn-sm" onclick="restartWorker('${w.id}')"><i class="fa-solid fa-rotate-right"></i> Restart</button>
                        <button class="btn btn-secondary btn-sm" onclick="openLogsModal('${w.id}', '${escapeHtml(w.config.stream_name)}')"><i class="fa-solid fa-terminal"></i> Logs</button>
                        <button class="btn btn-danger btn-sm" onclick="deleteWorker('${w.id}')"><i class="fa-solid fa-trash"></i> Delete</button>
                    </div>
                </div>
            `;
        }).join("");
    }

    window.setTally = async (id, state) => {
        await fetch(`/api/tally/${id}`, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ tallyState: state })
        });
        fetchWorkers();
    };

    openModalBtn.addEventListener("click", () => {
        // Auto-increment health/dest ports dynamically based on running count
        const existingCount = document.querySelectorAll(".worker-card").length;
        document.getElementById("healthPort").value = 8080 + existingCount;
        document.getElementById("destPort").value = 5004 + existingCount;
        spawnModal.classList.add("active");
    });
    closeModalBtn.addEventListener("click", () => spawnModal.classList.remove("active"));
    cancelModalBtn.addEventListener("click", () => spawnModal.classList.remove("active"));

    spawnForm.addEventListener("submit", async (e) => {
        e.preventDefault();
        const discIpPort = document.getElementById("discoveryServerIp").value.split(":");
        const payload = {
            stream_name: document.getElementById("streamName").value,
            source_type: document.getElementById("sourceType").value,
            source_uri: document.getElementById("sourceUri").value,
            discovery_mode: document.getElementById("discoveryMode").value,
            discovery_server_ip: discIpPort[0] || "127.0.0.1",
            discovery_server_port: parseInt(discIpPort[1] || "5959"),
            resolution: document.getElementById("resolution").value,
            fps: document.getElementById("fps").value,
            pattern: document.getElementById("pattern") ? document.getElementById("pattern").value : "smptebars",
            tally_state: document.getElementById("tallyState").value,
            umd_text: document.getElementById("streamName").value,
            audio_freq: parseInt(document.getElementById("audioFreq").value),
            overlay_text: document.getElementById("overlayText").value,
            dest_ip: document.getElementById("destIp").value,
            dest_port: parseInt(document.getElementById("destPort").value),
            health_port: parseInt(document.getElementById("healthPort").value)
        };

        try {
            const res = await fetch("/api/workers", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });
            if (res.ok) {
                spawnModal.classList.remove("active");
                fetchWorkers();
                fetchMetrics();
            }
        } catch (err) {
            console.error(err);
        }
    });

    window.startWorker = async (id) => {
        await fetch(`/api/workers/${id}/start`, { method: "POST" });
        fetchWorkers();
        fetchMetrics();
    };

    window.stopWorker = async (id) => {
        await fetch(`/api/workers/${id}/stop`, { method: "POST" });
        fetchWorkers();
        fetchMetrics();
    };

    window.restartWorker = async (id) => {
        await fetch(`/api/workers/${id}/restart`, { method: "POST" });
        fetchWorkers();
        fetchMetrics();
    };

    window.deleteWorker = async (id) => {
        if (confirm("Delete this NDI worker?")) {
            await fetch(`/api/workers/${id}`, { method: "DELETE" });
            fetchWorkers();
            fetchMetrics();
        }
    };

    window.openLogsModal = async (id, title) => {
        currentLogWorkerId = id;
        logsWorkerTitle.textContent = title;
        logsModal.classList.add("active");
        fetchLogs(id);
    };

    async function fetchLogs(id) {
        logsConsole.textContent = "Fetching container logs...";
        try {
            const res = await fetch(`/api/workers/${id}/logs`);
            if (res.ok) {
                const data = await res.json();
                logsConsole.textContent = data.logs || "No logs available";
            }
        } catch (err) {
            logsConsole.textContent = "Error loading logs";
        }
    }

    closeLogsBtn.addEventListener("click", () => logsModal.classList.remove("active"));
    dismissLogsBtn.addEventListener("click", () => logsModal.classList.remove("active"));
    refreshLogsBtn.addEventListener("click", () => {
        if (currentLogWorkerId) fetchLogs(currentLogWorkerId);
    });

    refreshBtn.addEventListener("click", () => {
        fetchMetrics();
        fetchWorkers();
    });

    function escapeHtml(str) {
        return (str || '').replace(/[&<>"']/g, m => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        })[m]);
    }

    fetchMetrics();
    fetchWorkers();
    setInterval(fetchMetrics, 5000);
    setInterval(fetchWorkers, 5000);
});
