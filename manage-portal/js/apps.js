// Per-agency Android apps tab.
// Lists every agency from crm_master with download links for its APK / AAB.
// Activated when the user clicks the "Android Apps" page tab in manage.html.

(function () {
    const pageTabs   = document.getElementById('page-tabs');
    const pageAgs    = document.getElementById('page-agencies');
    const pageApps   = document.getElementById('page-apps');
    const appsArea   = document.getElementById('apps-area');
    const refreshBtn = document.getElementById('apps-refresh-btn');

    if (!pageTabs || !pageApps) return;  // not on manage.html

    pageTabs.addEventListener('click', (e) => {
        const b = e.target.closest('button[data-page]');
        if (!b) return;
        pageTabs.querySelectorAll('button').forEach(x => x.classList.toggle('active', x === b));
        const page = b.dataset.page;
        pageAgs.classList.toggle('hidden',  page !== 'agencies');
        pageApps.classList.toggle('hidden', page !== 'apps');
        if (page === 'apps') loadApps();
    });

    refreshBtn.addEventListener('click', loadApps);

    function fmtSize(bytes) {
        if (!bytes) return '—';
        const mb = bytes / (1024 * 1024);
        return mb >= 1 ? mb.toFixed(1) + ' MB' : (bytes / 1024).toFixed(0) + ' KB';
    }

    // Builds a download URL that carries the manage token as a query string
    // (browsers do not attach custom headers to <a download>'s GET request).
    function downloadUrl(flavor, type) {
        const tok = sessionStorage.getItem('manage_token') || '';
        return `${API_BASE}/manage/apps/${encodeURIComponent(flavor)}/download/${type}?token=${encodeURIComponent(tok)}`;
    }

    async function loadApps() {
        appsArea.innerHTML = '<div class="empty"><div class="ico">⏳</div><div>Loading…</div></div>';
        try {
            const r = await api('/manage/apps');
            renderApps(r.apps || []);
        } catch (err) {
            appsArea.innerHTML =
                `<div class="empty"><div class="ico">⚠️</div><div>${escapeHtml(err.message || 'Failed to load apps')}</div></div>`;
        }
    }

    function renderApps(apps) {
        if (apps.length === 0) {
            appsArea.innerHTML =
                '<div class="empty"><div class="ico">📦</div><div>No agencies yet. Approve one first.</div></div>';
            return;
        }
        const rows = apps.map(a => {
            const statusPill = a.status === 'approved'
                ? '<span class="pill" style="background:#d1fae5;color:#065f46;">Approved</span>'
                : a.status === 'pending'
                    ? '<span class="pill" style="background:#fef3c7;color:#92400e;">Pending</span>'
                    : `<span class="pill" style="background:#fee2e2;color:#991b1b;">${escapeHtml(a.status)}</span>`;

            const apkCell = a.apkExists
                ? `<a class="btn btn-primary btn-sm" href="${downloadUrl(a.flavor, 'apk')}" download>↓ APK <span style="opacity:.7;font-weight:400;">${fmtSize(a.apkSize)}</span></a>
                   <div class="text-muted" style="font-size:11px;margin-top:4px;">built ${escapeHtml(a.apkBuiltAt)}</div>`
                : '<span class="text-muted" style="font-size:13px;">not built yet</span>';

            const aabCell = a.aabExists
                ? `<a class="btn btn-outline btn-sm" href="${downloadUrl(a.flavor, 'aab')}" download>↓ AAB <span style="opacity:.7;font-weight:400;">${fmtSize(a.aabSize)}</span></a>
                   <div class="text-muted" style="font-size:11px;margin-top:4px;">built ${escapeHtml(a.aabBuiltAt)}</div>`
                : '<span class="text-muted" style="font-size:13px;">not built yet</span>';

            const setupCell = a.setupExists
                ? `<a class="btn btn-primary btn-sm" href="${downloadUrl(a.flavor, 'setup')}" download>↓ Setup.exe <span style="opacity:.7;font-weight:400;">${fmtSize(a.setupSize)}</span></a>
                   <div class="text-muted" style="font-size:11px;margin-top:4px;">built ${escapeHtml(a.setupBuiltAt)}</div>`
                : '<span class="text-muted" style="font-size:13px;">not built yet</span>';

            const portableCell = a.portableExists
                ? `<a class="btn btn-outline btn-sm" href="${downloadUrl(a.flavor, 'portable')}" download>↓ Portable.zip <span style="opacity:.7;font-weight:400;">${fmtSize(a.portableSize)}</span></a>
                   <div class="text-muted" style="font-size:11px;margin-top:4px;">built ${escapeHtml(a.portableBuiltAt)}</div>`
                : '<span class="text-muted" style="font-size:13px;">not built yet</span>';

            const logoCell = a.logoUrl
                ? `<img src="${escapeHtml(a.logoUrl)}" alt=""
                       style="width:44px;height:44px;border-radius:8px;object-fit:cover;background:#fff;border:1px solid #e5e7eb;"
                       onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';">
                   <div style="display:none;width:44px;height:44px;border-radius:8px;background:#f3f4f6;align-items:center;justify-content:center;color:#9ca3af;font-size:11px;">—</div>`
                : `<div style="width:44px;height:44px;border-radius:8px;background:#f3f4f6;display:flex;align-items:center;justify-content:center;color:#9ca3af;font-size:11px;">no<br>logo</div>`;
            return `<tr>
                <td style="width:60px;">${logoCell}</td>
                <td>
                    <div style="font-weight:600;">${escapeHtml(a.name)}</div>
                    <div class="text-muted" style="font-size:12px;font-family:monospace;">${escapeHtml(a.packageId)}</div>
                </td>
                <td>${statusPill}</td>
                <td>${apkCell}</td>
                <td>${aabCell}</td>
                <td>${setupCell}</td>
                <td>${portableCell}</td>
            </tr>`;
        }).join('');

        appsArea.innerHTML = `
            <table class="data-table">
                <thead>
                    <tr>
                        <th></th>
                        <th>Agency</th>
                        <th>Status</th>
                        <th>Android Debug APK <span class="text-muted" style="font-weight:400;font-size:11px;">(side-load)</span></th>
                        <th>Android Release AAB <span class="text-muted" style="font-weight:400;font-size:11px;">(Play Console)</span></th>
                        <th>Windows Installer <span class="text-muted" style="font-weight:400;font-size:11px;">(side-by-side)</span></th>
                        <th>Portable ZIP <span class="text-muted" style="font-weight:400;font-size:11px;">(no install, AppLocker-safe)</span></th>
                    </tr>
                </thead>
                <tbody>${rows}</tbody>
            </table>`;
    }
})();
