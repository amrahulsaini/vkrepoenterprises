// Errors tab for manage.html — central client failure log.
// Lists crm_master.client_error_log: every failure the desktop apps hit is
// auto-reported there (POST /api/agency/desktop/client-error). Admin clicks a
// row to expand the full technical detail.

(function () {
    const pageTabs   = document.getElementById('page-tabs');
    const pageErrors = document.getElementById('page-errors');
    const area       = document.getElementById('errors-area');
    const refreshBtn = document.getElementById('errors-refresh-btn');
    const badge      = document.getElementById('errors-badge');
    if (!pageTabs || !pageErrors) return;  // not on manage.html

    pageTabs.addEventListener('click', (e) => {
        const b = e.target.closest('button[data-page]');
        if (!b) return;
        pageErrors.classList.toggle('hidden', b.dataset.page !== 'errors');
        if (b.dataset.page === 'errors') loadErrors();
    });
    refreshBtn.addEventListener('click', loadErrors);

    async function loadErrors() {
        area.innerHTML = '<div class="empty"><div class="ico">⏳</div><div>Loading…</div></div>';
        let list;
        try { list = await api('/manage/client-errors') || []; }
        catch (e) {
            area.innerHTML = `<div class="empty"><div class="ico">⚠️</div><div>Failed to load: ${escapeHtml(e.message)}</div></div>`;
            return;
        }
        render(list);

        // Badge = errors reported in the last 24h, so the admin notices fresh ones.
        const dayAgo = Date.now() - 86400000;
        const recent = list.filter(r => {
            const t = Date.parse(String(r.createdAt || '').replace(' ', 'T') + 'Z');
            return !isNaN(t) && t >= dayAgo;
        }).length;
        if (recent > 0) { badge.textContent = recent; badge.classList.remove('hidden'); }
        else badge.classList.add('hidden');
    }

    function render(list) {
        if (!list.length) {
            area.innerHTML = '<div class="empty"><div class="ico">✅</div><div>No errors reported. All clear.</div></div>';
            return;
        }
        const rows = list.map((r, i) => {
            const when = escapeHtml(r.createdAt || '');
            const ag   = escapeHtml(r.agencyName || r.agencySlug || '');
            const op   = escapeHtml(r.operation || '');
            const sum  = escapeHtml(r.summary || '');
            const mac  = escapeHtml(r.machineName || '');
            const detailText = [
                r.detail || '',
                r.context    ? ('\n\nContext: ' + r.context) : '',
                r.appVersion ? ('\nApp: v' + r.appVersion)   : '',
                r.os         ? ('\nOS: ' + r.os)             : '',
                r.sourceIp   ? ('\nIP: ' + r.sourceIp)       : '',
            ].join('');
            const detail = escapeHtml(detailText);
            return `
            <tr class="err-row" data-i="${i}" style="cursor:pointer;border-top:1px solid #eee;">
                <td style="padding:8px 10px;white-space:nowrap;color:#6b7280;font-size:12px;">${when}</td>
                <td style="padding:8px 10px;font-weight:600;">${ag}</td>
                <td style="padding:8px 10px;"><span class="pill" style="background:#fee2e2;color:#991b1b;">${op}</span></td>
                <td style="padding:8px 10px;">${sum}</td>
                <td style="padding:8px 10px;color:#6b7280;font-size:12px;white-space:nowrap;">${mac}</td>
            </tr>
            <tr class="err-detail hidden" data-d="${i}"><td colspan="5" style="padding:0 10px 12px;">
                <pre style="white-space:pre-wrap;background:#0b1020;color:#d1d5db;padding:12px;border-radius:8px;font-size:12px;overflow:auto;max-height:360px;margin:0;">${detail}</pre>
            </td></tr>`;
        }).join('');
        area.innerHTML = `
            <table style="width:100%;border-collapse:collapse;font-size:14px;">
                <thead><tr style="text-align:left;color:#6b7280;font-size:12px;">
                    <th style="padding:8px 10px;">When (UTC)</th>
                    <th style="padding:8px 10px;">Agency</th>
                    <th style="padding:8px 10px;">Operation</th>
                    <th style="padding:8px 10px;">What went wrong</th>
                    <th style="padding:8px 10px;">Machine</th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>`;
        area.querySelectorAll('.err-row').forEach(row => {
            row.addEventListener('click', () => {
                const d = area.querySelector(`.err-detail[data-d="${row.dataset.i}"]`);
                if (d) d.classList.toggle('hidden');
            });
        });
    }
})();
