document.addEventListener("DOMContentLoaded", () => {
    // DOM Elements
    const engineModeBadge = document.getElementById("engineModeBadge");
    const discoveryModeVal = document.getElementById("discoveryModeVal");
    const workerCountVal = document.getElementById("workerCountVal");
    const cpuUsageVal = document.getElementById("cpuUsageVal");
    const ramUsageVal = document.getElementById("ramUsageVal");
    const quadGrid = document.getElementById("quadGrid");
    const workerGrid = document.getElementById("workerGrid");
    const refreshBtn = document.getElementById("refreshBtn");

    // Audio Generator
    const audioToggleBtn = document.getElementById("audioToggleBtn");
    const audioStatusText = document.getElementById("audioStatusText");
    let audioCtx = null;
    let oscillator = null;
    let audioGain = null;
    let isAudioPlaying = false;

    // Modals
    const spawnModal = document.getElementById("spawnModal");
    const openModalBtn = document.getElementById("openModalBtn");
    const closeModalBtn = document.getElementById("closeModalBtn");
    const cancelModalBtn = document.getElementById("cancelModalBtn");
    const spawnForm = document.getElementById("spawnForm");

    const logsModal = document.getElementById("logsModal");
    const closeLogsBtn = document.getElementById("closeLogsBtn");
    const dismissLogsBtn = document.getElementById("dismissLogsBtn");
    const refreshLogsBtn = document.getElementById("refreshLogsBtn");
    const logsWorkerTitle = document.getElementById("logsWorkerTitle");
    const logsConsole = document.getElementById("logsConsole");

    let currentLogWorkerId = null;
    let isBackendConnected = false;

    // Default Initial Stream Workers State (for Demo / Standalone Mode)
    let localWorkers = [
        {
            id: "worker-demo-1",
            status: "running",
            config: {
                stream_name: "MEL-FINANCIAL-01",
                source_type: "WebPage",
                source_uri: "https://finance.yahoo.com",
                discovery_mode: "CentralServer",
                resolution: "1920x1080",
                fps: "30",
                tally_state: "Program",
                umd_text: "MEL-FINANCIAL-01",
                dest_ip: "239.255.0.1",
                dest_port: 5004,
                health_port: 8080,
                webrtc_url: "rtp://127.0.0.1:5004",
                overlay_text: "LIVE MARKET TICKER & CHARTS"
            }
        },
        {
            id: "worker-demo-2",
            status: "running",
            config: {
                stream_name: "MEL-CAM-TESTBARS",
                source_type: "ColorBars",
                source_uri: "",
                discovery_mode: "CentralServer",
                resolution: "1920x1080",
                fps: "30",
                tally_state: "Preview",
                umd_text: "SMPTE TEST BARS",
                dest_ip: "239.255.0.1",
                dest_port: 5005,
                health_port: 8081,
                overlay_text: "100% COLOR BARS & TIME"
            }
        },
        {
            id: "worker-demo-3",
            status: "running",
            config: {
                stream_name: "MEL-GRAPHICS-GRID",
                source_type: "Picture",
                source_uri: "assets/slideshow",
                discovery_mode: "Bonjour",
                resolution: "1280x720",
                fps: "60",
                tally_state: "Off",
                umd_text: "GRAPHICS FEED",
                dest_ip: "239.255.0.1",
                dest_port: 5006,
                health_port: 8082,
                overlay_text: "MEL BROADCAST OVERLAY"
            }
        },
        {
            id: "worker-demo-4",
            status: "running",
            config: {
                stream_name: "MEL-QUAD-MULTIVIEWER",
                source_type: "QuadSplit",
                source_uri: "MEL-FINANCIAL-01,MEL-CAM-TESTBARS,MEL-GRAPHICS-GRID,mandelbrot",
                discovery_mode: "CentralServer",
                resolution: "1920x1080",
                fps: "30",
                tally_state: "Program",
                umd_text: "QUAD 4-WAY SPLIT",
                dest_ip: "239.255.0.1",
                dest_port: 5007,
                health_port: 8083,
                overlay_text: "2x2 COMPOSITE MONITOR"
            }
        }
    ];

    // Quad Input Selections
    let quadSources = [
        "MEL-FINANCIAL-01",
        "MEL-CAM-TESTBARS",
        "MEL-GRAPHICS-GRID",
        "MEL-QUAD-MULTIVIEWER"
    ];

    let canvasAnimFrame = null;

    // --- Web Audio API Calibration Generator ---
    function toggleTestAudio() {
        if (!isAudioPlaying) {
            try {
                audioCtx = new (window.AudioContext || window.webkitAudioContext)();
                oscillator = audioCtx.createOscillator();
                audioGain = audioCtx.createGain();

                oscillator.type = "sine";
                oscillator.frequency.value = 1000;
                audioGain.gain.value = 0.15;

                oscillator.connect(audioGain);
                audioGain.connect(audioCtx.destination);
                oscillator.start();

                isAudioPlaying = true;
                if (audioToggleBtn) audioToggleBtn.classList.add("active");
                if (audioStatusText) audioStatusText.textContent = "1kHz Tone ON";
            } catch (err) {
                console.error("Audio Context Error:", err);
            }
        } else {
            if (oscillator) oscillator.stop();
            if (audioCtx) audioCtx.close();
            isAudioPlaying = false;
            if (audioToggleBtn) audioToggleBtn.classList.remove("active");
            if (audioStatusText) audioStatusText.textContent = "Test Tone OFF";
        }
    }

    if (audioToggleBtn) {
        audioToggleBtn.addEventListener("click", toggleTestAudio);
    }

    // --- Data Fetching & State Sync ---
    async function fetchMetrics() {
        try {
            const res = await fetch("/api/system/status");
            if (res.ok) {
                const data = await res.json();
                isBackendConnected = true;
                engineModeBadge.textContent = data.docker_available ? "Docker Engine" : "Process Fallback";
                engineModeBadge.className = `badge ${data.docker_available ? 'badge-green' : 'badge-yellow'}`;
                discoveryModeVal.textContent = "Central Server (127.0.0.1:5959)";
                workerCountVal.textContent = `${data.running_workers} / ${data.total_workers} Active`;
                cpuUsageVal.textContent = `${data.cpu_usage_percent}%`;
                ramUsageVal.textContent = `${data.memory_usage_mb} MB`;
            } else {
                useDemoMetrics();
            }
        } catch (err) {
            useDemoMetrics();
        }
    }

    function useDemoMetrics() {
        isBackendConnected = false;
        engineModeBadge.textContent = "Demo Mode";
        engineModeBadge.className = "badge badge-green";
        discoveryModeVal.textContent = "Central Server (127.0.0.1:5959)";
        const activeCount = localWorkers.filter(w => w.status === "running").length;
        workerCountVal.textContent = `${activeCount} / ${localWorkers.length} Active`;
        cpuUsageVal.textContent = `${Math.floor(8 + Math.random() * 8)}%`;
        ramUsageVal.textContent = `${170 + Math.floor(Math.random() * 20)} MB`;
    }

    async function fetchWorkers() {
        if (isBackendConnected) {
            try {
                const res = await fetch("/api/workers");
                if (res.ok) {
                    const workers = await res.json();
                    renderQuadGrid(workers);
                    renderWorkerGrid(workers);
                    return;
                }
            } catch (err) {
                console.error("API error, using local state", err);
            }
        }
        renderQuadGrid(localWorkers);
        renderWorkerGrid(localWorkers);
    }

    // --- Quad Multiviewer Rendering with Canvas ---
    function renderQuadGrid(workers) {
        quadGrid.innerHTML = "";

        const availableOptions = [
            `<option value="">-- Select Stream Source --</option>`,
            `<optgroup label="Active Streams">`,
            ...workers.map(w => `<option value="${escapeHtml(w.config.stream_name)}">${escapeHtml(w.config.stream_name)} (${w.config.source_type})</option>`),
            `</optgroup>`,
            `<optgroup label="Generators">`,
            `<option value="smptebars">SMPTE Color Bars</option>`,
            `<option value="marketchart">Market Ticker & Chart</option>`,
            `<option value="mandelbrot">Mandelbrot Fractal</option>`,
            `<option value="matrix">Matrix Code Feed</option>`,
            `</optgroup>`
        ].join("");

        for (let i = 0; i < 4; i++) {
            const selectedName = quadSources[i] || (workers[i] ? workers[i].config.stream_name : `smptebars`);
            const matchedWorker = workers.find(w => w.config.stream_name === selectedName);

            const tallyState = matchedWorker ? matchedWorker.config.tally_state : "Off";
            const tallyClass = tallyState === "Program" ? "tally-program" : tallyState === "Preview" ? "tally-preview" : "";
            const streamTitle = matchedWorker ? matchedWorker.config.stream_name : selectedName;
            const subText = matchedWorker ? `udp://${matchedWorker.config.dest_ip}:${matchedWorker.config.dest_port}` : `GENERATOR FEED`;

            const tileEl = document.createElement("div");
            tileEl.className = `quad-tile ${tallyClass}`;
            tileEl.innerHTML = `
                <div class="quad-viewport-container">
                    <div class="quad-overlay-header">
                        <span class="quad-label-badge"><i class="fa-solid fa-tv"></i> QUAD ${i + 1}</span>
                        <select class="quad-select" id="quadSelect-${i}">
                            ${availableOptions}
                        </select>
                    </div>
                    <canvas id="quadCanvas-${i}" class="quad-canvas" width="480" height="270"></canvas>
                </div>
                <div class="umd-bar">
                    <div style="display:flex; flex-direction:column;">
                        <span class="umd-label">${escapeHtml(matchedWorker ? (matchedWorker.config.umd_text || matchedWorker.config.stream_name) : streamTitle)}</span>
                        <span class="umd-sub">${subText}</span>
                    </div>
                    ${matchedWorker ? `
                    <div class="tally-controls">
                        <button class="tally-btn ${tallyState === 'Program' ? 'active-pgm' : ''}" onclick="setTally('${matchedWorker.id}', 'Program')">PGM</button>
                        <button class="tally-btn ${tallyState === 'Preview' ? 'active-pvw' : ''}" onclick="setTally('${matchedWorker.id}', 'Preview')">PVW</button>
                        <button class="tally-btn ${tallyState === 'Off' ? 'active-off' : ''}" onclick="setTally('${matchedWorker.id}', 'Off')">OFF</button>
                    </div>` : `<span class="badge badge-purple">Canvas Sync</span>`}
                </div>
            `;
            quadGrid.appendChild(tileEl);

            const selEl = tileEl.querySelector(`#quadSelect-${i}`);
            if (selEl) {
                selEl.value = selectedName;
                selEl.addEventListener("change", (e) => {
                    quadSources[i] = e.target.value;
                    fetchWorkers();
                });
            }
        }

        startCanvasGenerators(workers);
    }

    function startCanvasGenerators(workers) {
        if (canvasAnimFrame) cancelAnimationFrame(canvasAnimFrame);

        function drawLoop(timestamp) {
            for (let i = 0; i < 4; i++) {
                const canvas = document.getElementById(`quadCanvas-${i}`);
                if (!canvas) continue;
                const ctx = canvas.getContext("2d");
                const selected = quadSources[i] || "smptebars";
                const matchedWorker = workers.find(w => w.config.stream_name === selected);

                if (matchedWorker && matchedWorker.config.source_type === "WebPage") {
                    renderMarketTicker(ctx, canvas.width, canvas.height, matchedWorker.config.stream_name, timestamp);
                } else if (matchedWorker && matchedWorker.config.source_type === "ColorBars") {
                    renderSmpteBars(ctx, canvas.width, canvas.height, matchedWorker.config.stream_name, timestamp);
                } else if (matchedWorker && matchedWorker.config.source_type === "Picture") {
                    renderMandelbrot(ctx, canvas.width, canvas.height, timestamp);
                } else if (selected === "marketchart") {
                    renderMarketTicker(ctx, canvas.width, canvas.height, "LIVE MARKET CHART", timestamp);
                } else if (selected === "mandelbrot") {
                    renderMandelbrot(ctx, canvas.width, canvas.height, timestamp);
                } else if (selected === "matrix") {
                    renderMatrixRain(ctx, canvas.width, canvas.height, timestamp);
                } else {
                    renderSmpteBars(ctx, canvas.width, canvas.height, selected, timestamp);
                }
            }
            canvasAnimFrame = requestAnimationFrame(drawLoop);
        }

        canvasAnimFrame = requestAnimationFrame(drawLoop);
    }

    // Canvas Renderers
    function renderSmpteBars(ctx, width, height, label, time) {
        const colors = ["#ffffff", "#c0c000", "#00c0c0", "#00c000", "#c000c0", "#c00000", "#0000c0"];
        const barWidth = width / colors.length;

        colors.forEach((col, idx) => {
            ctx.fillStyle = col;
            ctx.fillRect(idx * barWidth, 0, barWidth, height * 0.75);
        });

        ctx.fillStyle = "#0000c0"; ctx.fillRect(0, height * 0.75, width * 0.25, height * 0.25);
        ctx.fillStyle = "#ffffff"; ctx.fillRect(width * 0.25, height * 0.75, width * 0.25, height * 0.25);
        ctx.fillStyle = "#000000"; ctx.fillRect(width * 0.5, height * 0.75, width * 0.5, height * 0.25);

        ctx.fillStyle = "rgba(0,0,0,0.65)";
        ctx.fillRect(10, height - 40, width - 20, 30);

        ctx.fillStyle = "#ffffff";
        ctx.font = "bold 13px sans-serif";
        const dateStr = new Date().toISOString().substring(11, 19);
        ctx.fillText(`[SMPTE] ${label.toUpperCase()} | ${dateStr}`, 18, height - 20);
    }

    function renderMarketTicker(ctx, width, height, label, time) {
        ctx.fillStyle = "#020617";
        ctx.fillRect(0, 0, width, height);

        ctx.fillStyle = "#0f172a";
        ctx.fillRect(0, 0, width, 32);
        ctx.fillStyle = "#38bdf8";
        ctx.font = "bold 12px sans-serif";
        ctx.fillText(`📊 ${label.toUpperCase()} — LIVE NASDAQ/CRYPTO TICKER`, 12, 20);

        const barCount = 18;
        const spacing = (width - 40) / barCount;
        for (let i = 0; i < barCount; i++) {
            const x = 20 + i * spacing;
            const h = 20 + Math.sin(time / 400 + i) * 25 + (i * 3) % 40;
            const isUp = Math.sin(time / 500 + i * 2) > 0;

            ctx.strokeStyle = isUp ? "#10b981" : "#ef4444";
            ctx.fillStyle = isUp ? "#10b981" : "#ef4444";

            ctx.beginPath();
            ctx.moveTo(x + 4, 50);
            ctx.lineTo(x + 4, 180);
            ctx.stroke();

            ctx.fillRect(x, 100 - h / 2, 8, h);
        }

        ctx.fillStyle = "#1e293b";
        ctx.fillRect(0, height - 36, width, 36);

        const offsetX = (time / 15) % (width + 300);
        ctx.fillStyle = "#f0f4fd";
        ctx.font = "bold 12px monospace";
        ctx.fillText(`BTC $87,420 ▲ +3.2%   |   ETH $3,120 ▲ +2.1%   |   NVDA $142 ▲ +4.8%   |   MEL-PROD ACTIVE`, width - offsetX, height - 13);
    }

    function renderMandelbrot(ctx, width, height, time) {
        ctx.fillStyle = "#0b0f19";
        ctx.fillRect(0, 0, width, height);

        const pulse = (Math.sin(time / 600) + 1) / 2;
        ctx.save();
        ctx.translate(width / 2, height / 2);
        ctx.rotate(time / 2000);

        for (let r = 100; r > 10; r -= 15) {
            ctx.strokeStyle = `hsl(${(time / 10 + r * 2) % 360}, 80%, 60%)`;
            ctx.lineWidth = 3;
            ctx.beginPath();
            ctx.arc(0, 0, r * (0.8 + pulse * 0.4), 0, Math.PI * 2);
            ctx.stroke();
        }
        ctx.restore();

        ctx.fillStyle = "#ffffff";
        ctx.font = "bold 13px sans-serif";
        ctx.fillText("🌀 MANDELBROT MOTION GENERATOR", 15, 25);
    }

    function renderMatrixRain(ctx, width, height, time) {
        ctx.fillStyle = "rgba(2, 6, 23, 0.2)";
        ctx.fillRect(0, 0, width, height);

        ctx.fillStyle = "#22c55e";
        ctx.font = "12px monospace";

        const columns = 20;
        for (let i = 0; i < columns; i++) {
            const char = String.fromCharCode(0x30A0 + Math.floor(Math.random() * 96));
            const x = i * (width / columns);
            const y = (time / 5 + i * 40) % height;
            ctx.fillText(char, x, y);
        }

        ctx.fillStyle = "#ffffff";
        ctx.font = "bold 12px sans-serif";
        ctx.fillText("💻 MATRIX NDI SIGNAL STREAM", 10, height - 15);
    }

    // --- Worker Management Table & Actions ---
    function renderWorkerGrid(workers) {
        if (!workers || workers.length === 0) {
            workerGrid.innerHTML = `
                <div style="grid-column: 1 / -1; text-align: center; color: var(--text-secondary); padding: 3rem; background: var(--card-bg); border-radius: 10px; border: 1px dashed var(--border-color);">
                    <i class="fa-solid fa-video-slash" style="font-size: 2.5rem; margin-bottom: 1rem; color: var(--text-muted);"></i>
                    <p style="font-size: 1.05rem; font-weight:600; color:var(--text-primary);">No NDI stream workers running.</p>
                    <p style="font-size:0.85rem; color:var(--text-secondary); margin-top:0.4rem;">Click <strong>"Spawn NDI Stream"</strong> above to launch a new broadcast docker stream.</p>
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
                ? `<span class="badge badge-purple"><i class="fa-solid fa-bolt"></i> WebRTC</span>`
                : "";

            return `
                <div class="worker-card" data-id="${w.id}">
                    <div class="card-header">
                        <span class="stream-title">
                            <i class="fa-solid fa-tower-broadcast text-accent"></i>
                            ${escapeHtml(w.config.stream_name)} ${webrtcBadge}
                        </span>
                        ${statusBadge}
                    </div>
                    <div class="card-body">
                        <div class="info-row"><span>Source Mode:</span> <span class="val">${w.config.source_type}</span></div>
                        <div class="info-row"><span>Source Path/URI:</span> <span class="val">${w.config.source_uri || '(Generator Internal)'}</span></div>
                        <div class="info-row"><span>Discovery Mode:</span> <span class="val">${w.config.discovery_mode}</span></div>
                        <div class="info-row"><span>Resolution & FPS:</span> <span class="val">${w.config.resolution} @ ${w.config.fps} fps</span></div>
                        <div class="info-row"><span>Tally / UMD:</span> <span class="val">${w.config.tally_state} (${w.config.umd_text})</span></div>
                        <div class="info-row"><span>Destination Stream:</span> <span class="val">udp://${w.config.dest_ip}:${w.config.dest_port}</span></div>
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

    // --- Worker Actions Callbacks ---
    window.setTally = async (id, state) => {
        if (isBackendConnected) {
            await fetch(`/api/tally/${id}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ tallyState: state })
            });
        } else {
            const worker = localWorkers.find(w => w.id === id);
            if (worker) worker.config.tally_state = state;
        }
        fetchWorkers();
    };

    window.startWorker = async (id) => {
        if (isBackendConnected) {
            await fetch(`/api/workers/${id}/start`, { method: "POST" });
        } else {
            const worker = localWorkers.find(w => w.id === id);
            if (worker) worker.status = "running";
        }
        fetchWorkers();
        fetchMetrics();
    };

    window.stopWorker = async (id) => {
        if (isBackendConnected) {
            await fetch(`/api/workers/${id}/stop`, { method: "POST" });
        } else {
            const worker = localWorkers.find(w => w.id === id);
            if (worker) worker.status = "stopped";
        }
        fetchWorkers();
        fetchMetrics();
    };

    window.restartWorker = async (id) => {
        if (isBackendConnected) {
            await fetch(`/api/workers/${id}/restart`, { method: "POST" });
        } else {
            const worker = localWorkers.find(w => w.id === id);
            if (worker) worker.status = "running";
        }
        fetchWorkers();
        fetchMetrics();
    };

    window.deleteWorker = async (id) => {
        if (confirm("Terminate and remove this NDI video stream worker?")) {
            if (isBackendConnected) {
                await fetch(`/api/workers/${id}`, { method: "DELETE" });
            } else {
                localWorkers = localWorkers.filter(w => w.id !== id);
            }
            fetchWorkers();
            fetchMetrics();
        }
    };

    // --- Spawn Modal Logic ---
    function openSpawnModal() {
        const existingCount = localWorkers.length;
        document.getElementById("healthPort").value = 8080 + existingCount;
        document.getElementById("destPort").value = 5004 + existingCount;
        spawnModal.classList.add("active");
    }

    if (openModalBtn) openModalBtn.addEventListener("click", openSpawnModal);
    if (closeModalBtn) closeModalBtn.addEventListener("click", () => spawnModal.classList.remove("active"));
    if (cancelModalBtn) cancelModalBtn.addEventListener("click", () => spawnModal.classList.remove("active"));

    if (spawnForm) {
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
                tally_state: document.getElementById("tallyState").value,
                umd_text: document.getElementById("streamName").value,
                audio_freq: parseInt(document.getElementById("audioFreq").value),
                overlay_text: document.getElementById("overlayText").value,
                dest_ip: document.getElementById("destIp").value,
                dest_port: parseInt(document.getElementById("destPort").value),
                health_port: parseInt(document.getElementById("healthPort").value)
            };

            if (isBackendConnected) {
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
            } else {
                const newId = `worker-user-${Date.now()}`;
                localWorkers.push({
                    id: newId,
                    status: "running",
                    config: payload
                });
                spawnModal.classList.remove("active");
                fetchWorkers();
                fetchMetrics();
            }
        });
    }

    // --- Logs Modal Logic ---
    window.openLogsModal = async (id, title) => {
        currentLogWorkerId = id;
        logsWorkerTitle.textContent = title;
        logsModal.classList.add("active");
        fetchLogs(id);
    };

    async function fetchLogs(id) {
        logsConsole.textContent = "Fetching container stdout/stderr logs...";
        if (isBackendConnected) {
            try {
                const res = await fetch(`/api/workers/${id}/logs`);
                if (res.ok) {
                    const data = await res.json();
                    logsConsole.textContent = data.logs || "No container logs available.";
                    return;
                }
            } catch (err) {
                console.error(err);
            }
        }

        const worker = localWorkers.find(w => w.id === id);
        const name = worker ? worker.config.stream_name : id;
        const now = new Date().toISOString();
        logsConsole.textContent = [
            `[${now}] INFO  [NdiWorker.Program] Starting NDI Video Worker Stream: ${name}`,
            `[${now}] INFO  [NdiWorker.Native] Initializing NDI SDK Native Library (libndi.so.5)...`,
            `[${now}] SUCCESS [NdiWorker.Native] NDI SDK initialized successfully.`,
            `[${now}] INFO  [NdiWorker.Discovery] Connecting to NDI Central Discovery Server (127.0.0.1:5959)...`,
            `[${now}] SUCCESS [NdiWorker.Discovery] Registered stream '${name}' with Central Server.`,
            `[${now}] INFO  [NdiWorker.Engine] Frame Generator active: ${worker ? worker.config.source_type : 'WebPage'} @ ${worker ? worker.config.resolution : '1080p'}`,
            `[${now}] INFO  [NdiWorker.Health] HTTP Health listener listening on http://0.0.0.0:${worker ? worker.config.health_port : 8080}/health`,
            `[${now}] INFO  [NdiWorker.Stream] Broadcasting UYVY frames to destination udp://${worker ? worker.config.dest_ip : '239.255.0.1'}:${worker ? worker.config.dest_port : 5004}...`
        ].join("\n");
    }

    if (closeLogsBtn) closeLogsBtn.addEventListener("click", () => logsModal.classList.remove("active"));
    if (dismissLogsBtn) dismissLogsBtn.addEventListener("click", () => logsModal.classList.remove("active"));
    if (refreshLogsBtn) refreshLogsBtn.addEventListener("click", () => {
        if (currentLogWorkerId) fetchLogs(currentLogWorkerId);
    });

    if (refreshBtn) {
        refreshBtn.addEventListener("click", () => {
            fetchMetrics();
            fetchWorkers();
        });
    }

    function escapeHtml(str) {
        return (str || '').replace(/[&<>"']/g, m => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        })[m]);
    }

    // Initialize
    fetchMetrics();
    fetchWorkers();
    setInterval(fetchMetrics, 5000);
});
