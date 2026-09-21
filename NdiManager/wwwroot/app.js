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
        for (let i = 0; i < 4; i++) {
            const w = workers[i];
            if (w) {
                const tallyClass = w.config.tally_state === "Program" ? "tally-program"
                                 : w.config.tally_state === "Preview" ? "tally-preview" : "";

                const tallyBadgeClass = w.config.tally_state === "Program" ? "tally-badge-program"
                                      : w.config.tally_state === "Preview" ? "tally-badge-preview" : "tally-badge-off";

                quadGrid.innerHTML += `
                    <div class="quad-tile ${tallyClass}">
                        <div class="quad-screen">
                            <i class="fa-solid fa-broadcast-tower"></i>
                            <span style="font-weight:700; color:#fff;">${escapeHtml(w.config.stream_name)}</span>
                            <span>${w.config.source_type} | ${w.config.resolution} @ ${w.config.fps}fps</span>
                            <div class="tally-controls">
                                <button class="tally-btn ${w.config.tally_state === 'Program' ? 'active-pgm' : ''}" onclick="setTally('${w.id}', 'Program')">PGM</button>
                                <button class="tally-btn ${w.config.tally_state === 'Preview' ? 'active-pvw' : ''}" onclick="setTally('${w.id}', 'Preview')">PVW</button>
                                <button class="tally-btn" onclick="setTally('${w.id}', 'Off')">OFF</button>
                            </div>
                        </div>
                        <div class="umd-bar">
                            <span class="umd-label">${escapeHtml(w.config.umd_text || w.config.stream_name)}</span>
                            <span class="tally-badge ${tallyBadgeClass}">${w.config.tally_state}</span>
                            <span class="umd-sub">udp://${w.config.dest_ip}:${w.config.dest_port}</span>
                        </div>
                    </div>
                `;
            } else {
                quadGrid.innerHTML += `
                    <div class="quad-tile empty">
                        <div class="quad-screen">
                            <i class="fa-solid fa-tv"></i>
                            <span>Quad ${i + 1}: Unassigned Slot</span>
                        </div>
                        <div class="umd-bar">
                            <span class="umd-label">CAM ${i + 1}</span>
                            <span class="umd-sub">NO FEED</span>
                        </div>
                    </div>
                `;
            }
        }
    }

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

            return `
                <div class="worker-card" data-id="${w.id}">
                    <div class="card-header">
                        <span class="stream-title"><i class="fa-solid fa-broadcast-tower"></i> ${escapeHtml(w.config.stream_name)}</span>
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
            discovery_server_ip: discIpPort[0] || "10.10.1.1",
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
