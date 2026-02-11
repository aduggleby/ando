// =============================================================================
// project-details.js
//
// Summary: Live updates for the project details page.
//
// Polls project details periodically so builds triggered in the background
// (webhooks, scheduled jobs, etc.) appear without manual refresh.
// =============================================================================

(function () {
    const liveStatusEl = document.getElementById('project-live-status');
    if (!liveStatusEl) {
        return;
    }

    const projectId = Number(liveStatusEl.dataset.projectId);
    if (!Number.isFinite(projectId) || projectId <= 0) {
        return;
    }

    const totalBuildsEl = document.getElementById('project-total-builds');
    const buildsContainerEl = document.getElementById('project-builds-container');
    const liveStatusLinkEl = document.getElementById('project-live-status-link');
    const liveStatusUrlTemplate = liveStatusEl.dataset.liveStatusUrlTemplate || '/builds/__BUILD_ID__';

    let isPolling = false;
    let stopped = false;
    let timerId = null;

    function escapeHtml(value) {
        return String(value)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    function truncate(value, maxLength) {
        if (!value) {
            return '';
        }

        return value.length > maxLength ? `${value.slice(0, maxLength - 3)}...` : value;
    }

    function formatDuration(duration) {
        if (!duration) {
            return '-';
        }

        const totalSeconds = Math.max(0, Math.floor(parseDurationToSeconds(duration)));
        if (totalSeconds < 60) {
            return `${totalSeconds}s`;
        }

        if (totalSeconds < 3600) {
            const minutes = Math.floor(totalSeconds / 60);
            const seconds = totalSeconds % 60;
            return `${minutes}m ${seconds}s`;
        }

        const hours = Math.floor(totalSeconds / 3600);
        const remainingSeconds = totalSeconds % 3600;
        const minutes = Math.floor(remainingSeconds / 60);
        return `${hours}h ${minutes}m`;
    }

    function parseDurationToSeconds(duration) {
        if (typeof duration === 'number') {
            return duration;
        }

        if (typeof duration !== 'string') {
            return 0;
        }

        const parts = duration.split(':').map(Number);
        if (parts.some(Number.isNaN)) {
            return 0;
        }

        if (parts.length === 3) {
            return (parts[0] * 3600) + (parts[1] * 60) + parts[2];
        }

        if (parts.length === 2) {
            return (parts[0] * 60) + parts[1];
        }

        return parts[0] || 0;
    }

    function formatRelativeTime(value) {
        if (!value) {
            return '-';
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return '-';
        }

        const diffMs = Date.now() - date.getTime();
        const diffMinutes = diffMs / 60000;

        if (diffMinutes < 1) {
            return 'just now';
        }

        if (diffMinutes < 60) {
            return `${Math.floor(diffMinutes)}m ago`;
        }

        const diffHours = diffMinutes / 60;
        if (diffHours < 24) {
            return `${Math.floor(diffHours)}h ago`;
        }

        const diffDays = diffHours / 24;
        if (diffDays < 7) {
            return `${Math.floor(diffDays)}d ago`;
        }

        return date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
    }

    function renderBuildRow(build) {
        const status = build.status || '';
        const statusClass = `status-${String(status).toLowerCase()}`;
        const buildUrl = `/builds/${build.id}`;
        const commitSha = build.shortCommitSha || (build.commitSha ? String(build.commitSha).slice(0, 8) : '-');
        const branch = build.branch || '-';
        const commitMessage = truncate(build.commitMessage || '', 60);
        const trigger = build.trigger || '-';

        const prBadge = build.pullRequestNumber
            ? `<span class="pr-badge">#${escapeHtml(build.pullRequestNumber)}</span>`
            : '';

        return `
            <tr class="build-row" onclick="window.location='${buildUrl}'">
                <td>
                    <span class="status-badge ${statusClass}">${escapeHtml(status)}</span>
                </td>
                <td>
                    <code class="commit-sha">${escapeHtml(commitSha)}</code>
                </td>
                <td>
                    <span class="branch-name">${escapeHtml(branch)}</span>
                    ${prBadge}
                </td>
                <td class="commit-message">${escapeHtml(commitMessage)}</td>
                <td>${escapeHtml(trigger)}</td>
                <td>${escapeHtml(formatDuration(build.duration))}</td>
                <td>${escapeHtml(formatRelativeTime(build.queuedAt))}</td>
                <td><a href="${buildUrl}" class="build-view-link" onclick="event.stopPropagation()">View</a></td>
            </tr>
        `;
    }

    function ensureBuildTable() {
        const existingTable = document.getElementById('project-builds-table');
        const existingTbody = document.getElementById('project-builds-tbody');
        if (existingTable && existingTbody) {
            return { table: existingTable, tbody: existingTbody };
        }

        if (!buildsContainerEl) {
            return null;
        }

        const table = document.createElement('table');
        table.className = 'builds-table';
        table.id = 'project-builds-table';
        table.innerHTML = `
            <thead>
                <tr>
                    <th>Status</th>
                    <th>Commit</th>
                    <th>Branch</th>
                    <th>Message</th>
                    <th>Trigger</th>
                    <th>Duration</th>
                    <th>Queued</th>
                    <th><span class="sr-only">Actions</span></th>
                </tr>
            </thead>
            <tbody id="project-builds-tbody"></tbody>
        `;

        buildsContainerEl.appendChild(table);
        return {
            table,
            tbody: table.querySelector('#project-builds-tbody')
        };
    }

    function updateLiveStatus(recentBuilds) {
        const newestBuild = Array.isArray(recentBuilds) && recentBuilds.length > 0 ? recentBuilds[0] : null;
        const status = newestBuild?.status;
        const isActive = status === 'Queued' || status === 'Running';

        if (!isActive || !newestBuild) {
            liveStatusEl.style.display = 'none';
            return;
        }

        const buildUrl = liveStatusUrlTemplate.replace('__BUILD_ID__', String(newestBuild.id));
        if (liveStatusLinkEl) {
            liveStatusLinkEl.href = buildUrl;
        }

        liveStatusEl.style.display = '';
    }

    function updateBuildTable(recentBuilds) {
        if (!Array.isArray(recentBuilds) || !buildsContainerEl) {
            return;
        }

        if (recentBuilds.length === 0) {
            const table = document.getElementById('project-builds-table');
            if (table) {
                table.remove();
            }

            if (!document.getElementById('project-builds-empty-state')) {
                const empty = document.createElement('div');
                empty.id = 'project-builds-empty-state';
                empty.className = 'empty-state';
                empty.innerHTML = '<p>No builds yet. Trigger a manual build or push to the repository.</p>';
                buildsContainerEl.appendChild(empty);
            }

            buildsContainerEl.dataset.empty = 'true';
            return;
        }

        const empty = document.getElementById('project-builds-empty-state');
        if (empty) {
            empty.remove();
        }

        const tableParts = ensureBuildTable();
        if (!tableParts?.tbody) {
            return;
        }

        tableParts.tbody.innerHTML = recentBuilds.map(renderBuildRow).join('');
        buildsContainerEl.dataset.empty = 'false';
    }

    function updateFromResponse(project) {
        if (!project) {
            return;
        }

        if (totalBuildsEl && Number.isFinite(project.totalBuilds)) {
            totalBuildsEl.textContent = String(project.totalBuilds);
        }

        updateLiveStatus(project.recentBuilds);
        updateBuildTable(project.recentBuilds || []);
    }

    async function pollProject() {
        if (stopped || isPolling) {
            return;
        }

        isPolling = true;

        try {
            const response = await fetch(`/api/projects/${projectId}`, {
                method: 'GET',
                credentials: 'same-origin',
                headers: {
                    Accept: 'application/json'
                }
            });

            if (response.status === 401 || response.status === 403) {
                stopped = true;
                return;
            }

            if (!response.ok) {
                return;
            }

            const payload = await response.json();
            updateFromResponse(payload.project);
        } catch {
            // Keep polling through transient network issues.
        } finally {
            isPolling = false;
        }
    }

    function scheduleNextPoll(delayMs) {
        clearTimeout(timerId);
        timerId = setTimeout(async () => {
            if (!document.hidden) {
                await pollProject();
            }

            scheduleNextPoll(5000);
        }, delayMs);
    }

    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
            void pollProject();
        }
    });

    void pollProject();
    scheduleNextPoll(5000);
})();
