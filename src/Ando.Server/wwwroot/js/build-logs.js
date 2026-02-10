// =============================================================================
// build-logs.js
//
// Summary: SignalR client for real-time build log streaming.
//
// Connects to the BuildLogHub and receives log entries in real-time.
// Handles reconnection and catch-up with missed logs.
// Includes log tools: search/filter, jump to error, copy logs.
// =============================================================================

let connection = null;
let buildId = null;
let lastSequence = 0;
let autoScroll = true;
let reconnectAttempts = 0;
const maxReconnectAttempts = 10;
let pollTimer = null;
const pollIntervalMs = 2000;
let signalRDisabled = false;
let signalRFailureLogged = false;

// Step progress tracking
let stepsTotal = 0;
let stepsCompleted = 0;
let stepsFailed = 0;

// Log filtering
let searchFilter = "";

// ANSI color code to CSS class mapping
const ansiColors = {
    30: "ansi-black", 31: "ansi-red", 32: "ansi-green", 33: "ansi-yellow",
    34: "ansi-blue", 35: "ansi-magenta", 36: "ansi-cyan", 37: "ansi-white",
    90: "ansi-bright-black", 91: "ansi-bright-red", 92: "ansi-bright-green", 93: "ansi-bright-yellow",
    94: "ansi-bright-blue", 95: "ansi-bright-magenta", 96: "ansi-bright-cyan", 97: "ansi-bright-white"
};

// Parse ANSI escape codes and convert to HTML with CSS classes
function parseAnsi(text) {
    // Escape HTML first to prevent XSS
    const escaped = text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");

    // Match ANSI escape sequences: ESC[...m (where ESC is \x1b or \u001b)
    const ansiRegex = /\x1b\[([0-9;]*)m|\u001b\[([0-9;]*)m|\[([0-9;]*)m/g;

    let result = "";
    let lastIndex = 0;
    let currentClasses = [];
    let match;

    while ((match = ansiRegex.exec(escaped)) !== null) {
        // Add text before this escape sequence
        if (match.index > lastIndex) {
            const textBefore = escaped.slice(lastIndex, match.index);
            if (currentClasses.length > 0) {
                result += `<span class="${currentClasses.join(" ")}">${textBefore}</span>`;
            } else {
                result += textBefore;
            }
        }

        // Parse the codes (could be multiple separated by ;)
        const codes = (match[1] || match[2] || match[3] || "0").split(";").map(Number);

        for (const code of codes) {
            if (code === 0) {
                // Reset
                currentClasses = [];
            } else if (code === 1) {
                // Bold
                if (!currentClasses.includes("ansi-bold")) currentClasses.push("ansi-bold");
            } else if (code === 2) {
                // Dim
                if (!currentClasses.includes("ansi-dim")) currentClasses.push("ansi-dim");
            } else if (ansiColors[code]) {
                // Remove any existing color class and add new one
                currentClasses = currentClasses.filter(c => !c.startsWith("ansi-") || c === "ansi-bold" || c === "ansi-dim");
                currentClasses.push(ansiColors[code]);
            }
        }

        lastIndex = match.index + match[0].length;
    }

    // Add remaining text
    if (lastIndex < escaped.length) {
        const remaining = escaped.slice(lastIndex);
        if (currentClasses.length > 0) {
            result += `<span class="${currentClasses.join(" ")}">${remaining}</span>`;
        } else {
            result += remaining;
        }
    }

    return result || escaped;
}

// Initialize the SignalR connection for build logs
function initializeBuildLogs(id, initialSequence, stepInfo = {}) {
    buildId = id;
    lastSequence = initialSequence;
    stepsTotal = stepInfo.stepsTotal || 0;
    stepsCompleted = stepInfo.stepsCompleted || 0;
    stepsFailed = stepInfo.stepsFailed || 0;

    connection = new signalR.HubConnectionBuilder()
        // Prefer WebSockets. Some proxies break SignalR fallback transports (SSE/long polling)
        // by terminating long-lived requests, which causes "No Connection with that ID".
        .withUrl("/hubs/build-logs", {
            skipNegotiation: true,
            transport: signalR.HttpTransportType.WebSockets
        })
        .withAutomaticReconnect({
            nextRetryDelayInMilliseconds: (retryContext) => {
                if (retryContext.previousRetryCount < 5) {
                    return 1000 * Math.pow(2, retryContext.previousRetryCount);
                }
                return 30000;
            }
        })
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // Handle incoming log entries
    connection.on("LogEntry", (entry) => {
        if (entry.sequence > lastSequence) {
            appendLogEntry(entry);
            lastSequence = entry.sequence;
        }
    });

    // Handle build completion
    connection.on("BuildCompleted", (status) => {
        updateBuildStatus(status);
        hideLiveIndicator();
    });

    // Handle reconnection
    connection.onreconnected(() => {
        console.log("Reconnected to build logs hub");
        reconnectAttempts = 0;
        catchUpLogs();
    });

    connection.onreconnecting(() => {
        console.log("Reconnecting to build logs hub...");
    });

    connection.onclose((error) => {
        if (error) {
            console.error("Connection closed with error:", error);
            attemptReconnect();
        }
    });

    // Start connection
    startConnection();

    // Also poll for log updates. This makes "live logs" work even when
    // websockets are blocked by a proxy or SignalR cannot connect.
    startPolling();
}

// Start the SignalR connection
async function startConnection() {
    try {
        if (signalRDisabled) return;

        await connection.start();
        console.log("Connected to build logs hub");
        reconnectAttempts = 0;

        // Join the build's group
        await connection.invoke("JoinBuildLog", buildId);

        // Catch up on any missed logs
        await catchUpLogs();
    } catch (error) {
        // If WebSockets are blocked, don't spam retries; polling will keep logs moving.
        if (!signalRFailureLogged) {
            signalRFailureLogged = true;
            console.warn("SignalR unavailable (using polling for logs).", error);
        }
        signalRDisabled = true;
    }
}

// Attempt to reconnect after connection failure
function attemptReconnect() {
    if (signalRDisabled) return;

    if (reconnectAttempts >= maxReconnectAttempts) {
        console.error("Max reconnection attempts reached");
        showConnectionError();
        return;
    }

    reconnectAttempts++;
    const delay = Math.min(1000 * Math.pow(2, reconnectAttempts), 30000);

    console.log(`Reconnecting in ${delay}ms (attempt ${reconnectAttempts})`);
    setTimeout(startConnection, delay);
}

// Fetch any logs we might have missed
async function catchUpLogs() {
    try {
        // Prefer MVC endpoint over /api to avoid proxies that strip cookies/headers on /api.
        const response = await fetch(`/builds/${buildId}/logs?afterSequence=${lastSequence}`, {
            cache: "no-store",
            credentials: "same-origin"
        });

        if (!response.ok) {
            // Not authorized (or redirected to login) - stop polling and prompt reload.
            if (response.status === 401 || response.status === 403) {
                stopPolling();
                showConnectionError();
            }
            return;
        }

        const contentType = response.headers.get("content-type") || "";
        if (!contentType.includes("application/json")) {
            // Likely got HTML (e.g., login page after redirect). Treat as disconnected.
            stopPolling();
            showConnectionError();
            return;
        }

        const data = await response.json();

        for (const entry of data.logs) {
            if (entry.sequence > lastSequence) {
                appendLogEntry(entry);
                lastSequence = entry.sequence;
            }
        }

        if (data.isComplete) {
            updateBuildStatus(data.status);
            hideLiveIndicator();
            stopPolling();
        }
    } catch (error) {
        console.error("Failed to catch up logs:", error);
    }
}

function startPolling() {
    if (pollTimer) return;
    pollTimer = setInterval(() => {
        // Fire and forget; errors are handled inside catchUpLogs.
        catchUpLogs();
    }, pollIntervalMs);
}

function stopPolling() {
    if (!pollTimer) return;
    clearInterval(pollTimer);
    pollTimer = null;
}

// Append a log entry to the log container
function appendLogEntry(entry) {
    const container = document.getElementById("log-entries");
    const placeholder = document.getElementById("log-placeholder");

    if (placeholder) {
        placeholder.remove();
    }

    const div = document.createElement("div");
    div.className = `log-entry log-${entry.type.toLowerCase()}`;
    div.dataset.sequence = entry.sequence;
    div.dataset.type = entry.type.toLowerCase();
    if (entry.stepName) {
        div.dataset.step = entry.stepName.toLowerCase();
    }

    const time = document.createElement("span");
    time.className = "log-time";
    time.textContent = formatTime(entry.timestamp);
    div.appendChild(time);

    if (entry.stepName) {
        const step = document.createElement("span");
        step.className = "log-step";
        step.textContent = `[${entry.stepName}]`;
        div.appendChild(step);
    }

    const message = document.createElement("span");
    message.className = "log-message";
    message.innerHTML = parseAnsi(entry.message);
    div.appendChild(message);

    container.appendChild(div);

    // Update step progress if this is a step event
    if (entry.type === "StepCompleted") {
        stepsCompleted++;
        updateStepsProgress();
    } else if (entry.type === "StepFailed") {
        stepsCompleted++;
        stepsFailed++;
        updateStepsProgress();
    }

    // Apply search filter if active
    if (searchFilter) {
        applySearchFilter();
    }

    if (autoScroll) {
        scrollToBottom();
    }
}

// Update the steps progress bar
function updateStepsProgress() {
    const progressFill = document.getElementById("steps-progress-fill");
    const stepsCount = document.getElementById("steps-count");

    if (progressFill && stepsTotal > 0) {
        const percent = Math.round((stepsCompleted / stepsTotal) * 100);
        progressFill.style.width = `${percent}%`;

        if (stepsFailed > 0) {
            progressFill.classList.add("steps-progress-fill-failed");
        }
    }

    if (stepsCount && stepsTotal > 0) {
        let text = `${stepsCompleted} / ${stepsTotal} steps`;
        if (stepsFailed > 0) {
            text += ` <span class="text-error">(${stepsFailed} failed)</span>`;
        }
        stepsCount.innerHTML = text;
    }
}

// Log tools: Search/filter logs
function filterLogs(query) {
    searchFilter = query.toLowerCase();
    applySearchFilter();
}

function applySearchFilter() {
    const entries = document.querySelectorAll(".log-entry");
    entries.forEach(entry => {
        const message = entry.querySelector(".log-message")?.textContent?.toLowerCase() || "";
        const step = entry.dataset.step || "";
        const matches = !searchFilter || message.includes(searchFilter) || step.includes(searchFilter);
        entry.style.display = matches ? "" : "none";
    });
}

// Log tools: Jump to first error
function jumpToFirstError() {
    const errorEntry = document.querySelector(".log-entry.log-error, .log-entry.log-stepfailed");
    if (errorEntry) {
        errorEntry.scrollIntoView({ behavior: "smooth", block: "center" });
        errorEntry.classList.add("ring-2", "ring-red-500");
        setTimeout(() => errorEntry.classList.remove("ring-2", "ring-red-500"), 2000);
    }
}

// Log tools: Copy all logs to clipboard
async function copyLogs() {
    const entries = document.querySelectorAll(".log-entry");
    const lines = [];

    entries.forEach(entry => {
        const time = entry.querySelector(".log-time")?.textContent || "";
        const step = entry.querySelector(".log-step")?.textContent || "";
        const message = entry.querySelector(".log-message")?.textContent || "";
        lines.push(`${time} ${step} ${message}`.trim());
    });

    try {
        await navigator.clipboard.writeText(lines.join("\n"));
        showCopyFeedback("Logs copied to clipboard!");
    } catch (err) {
        console.error("Failed to copy logs:", err);
        showCopyFeedback("Failed to copy logs", true);
    }
}

function showCopyFeedback(message, isError = false) {
    const btn = document.getElementById("copy-logs-btn");
    if (btn) {
        const originalText = btn.textContent;
        btn.textContent = message;
        btn.classList.add(isError ? "text-error" : "text-success");
        setTimeout(() => {
            btn.textContent = originalText;
            btn.classList.remove("text-error", "text-success");
        }, 2000);
    }
}

// Update the build status display
function updateBuildStatus(status) {
    const badge = document.getElementById("build-status");
    if (badge) {
        badge.textContent = status;
        badge.className = `status-badge status-${status.toLowerCase()}`;
    }

    // Update action buttons
    updateActionButtons(status);
}

// Update action buttons based on new status
function updateActionButtons(status) {
    const canCancel = status === "Queued" || status === "Running";
    const canRetry = status === "Failed" || status === "Cancelled" || status === "TimedOut";

    // This is a simple approach - a full implementation would dynamically
    // add/remove the form elements. For now, page reload is recommended.
    if (!canCancel && !canRetry) {
        // Build is complete, suggest page reload for updated actions
        console.log("Build complete. Refresh page for updated actions.");
    }
}

// Hide the live indicator
function hideLiveIndicator() {
    const indicator = document.getElementById("live-indicator");
    if (indicator) {
        indicator.style.display = "none";
    }

    const scrollToggle = document.getElementById("scroll-toggle");
    if (scrollToggle) {
        scrollToggle.style.display = "none";
    }
}

// Show connection error message
function showConnectionError() {
    const container = document.getElementById("log-container");
    const errorDiv = document.createElement("div");
    errorDiv.className = "connection-error";
    errorDiv.innerHTML = `
        <p>Connection lost. <a href="javascript:location.reload()">Reload page</a> to reconnect.</p>
    `;
    container.insertBefore(errorDiv, container.firstChild);
}

// Toggle auto-scroll
function toggleAutoScroll() {
    autoScroll = !autoScroll;
    const button = document.getElementById("scroll-toggle");
    if (button) {
        button.textContent = `Auto-scroll: ${autoScroll ? "On" : "Off"}`;
    }

    if (autoScroll) {
        scrollToBottom();
    }
}

// Scroll to bottom of log container
function scrollToBottom() {
    const container = document.getElementById("log-container");
    if (container) {
        container.scrollTop = container.scrollHeight;
    }
}

// Format timestamp for display
function formatTime(timestamp) {
    const date = new Date(timestamp);
    const hours = date.getHours().toString().padStart(2, "0");
    const minutes = date.getMinutes().toString().padStart(2, "0");
    const seconds = date.getSeconds().toString().padStart(2, "0");
    const ms = date.getMilliseconds().toString().padStart(3, "0");
    return `${hours}:${minutes}:${seconds}.${ms}`;
}

// Cleanup when leaving the page
window.addEventListener("beforeunload", () => {
    stopPolling();
    if (connection) {
        connection.stop();
    }
});
