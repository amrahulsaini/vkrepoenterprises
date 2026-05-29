// Support Tickets tab for manage.html.
// Lists every agency's support ticket from crm_master.support_tickets; the
// admin opens one to view the message + screenshot, set status, and reply.
// The agency sees the reply + status back in their desktop app.

(function () {
    const pageTabs    = document.getElementById('page-tabs');
    const pageTickets = document.getElementById('page-tickets');
    const ticketsArea = document.getElementById('tickets-area');
    const refreshBtn  = document.getElementById('tickets-refresh-btn');
    const badge       = document.getElementById('tickets-badge');
    if (!pageTabs || !pageTickets) return;  // not on manage.html

    // Modal els
    const modal   = document.getElementById('ticket-modal');
    const tkTitle = document.getElementById('tk-title');
    const tkMeta  = document.getElementById('tk-meta');
    const tkMsg   = document.getElementById('tk-message');
    const tkShotW = document.getElementById('tk-shot-wrap');
    const tkShot  = document.getElementById('tk-shot');
    const tkShotL = document.getElementById('tk-shot-link');
    const tkStat  = document.getElementById('tk-status');
    const tkReply = document.getElementById('tk-reply');
    const tkSave  = document.getElementById('tk-save');
    const tkCancel= document.getElementById('tk-cancel');

    let _open = null;   // ticket currently open in the modal
    let _cache = [];

    pageTabs.addEventListener('click', (e) => {
        const b = e.target.closest('button[data-page]');
        if (!b) return;
        const page = b.dataset.page;
        pageTickets.classList.toggle('hidden', page !== 'tickets');
        if (page === 'tickets') loadTickets();
    });
    refreshBtn.addEventListener('click', loadTickets);

    // Badge: load once on startup so the "open" count shows without opening the tab.
    refreshBadge();

    async function refreshBadge() {
        try {
            const list = await api('/manage/tickets');
            const open = (list || []).filter(t => t.status !== 'resolved').length;
            if (open > 0) { badge.textContent = open; badge.classList.remove('hidden'); }
            else badge.classList.add('hidden');
        } catch { /* not logged in yet — ignore */ }
    }

    function statusPill(s) {
        if (s === 'resolved')    return '<span class="pill" style="background:#d1fae5;color:#065f46;">Resolved</span>';
        if (s === 'in_progress') return '<span class="pill" style="background:#fef3c7;color:#92400e;">In progress</span>';
        return '<span class="pill" style="background:#e0e7ff;color:#3730a3;">Open</span>';
    }

    async function loadTickets() {
        ticketsArea.innerHTML = '<div class="empty"><div class="ico">⏳</div><div>Loading…</div></div>';
        try {
            _cache = await api('/manage/tickets') || [];
            render(_cache);
            const open = _cache.filter(t => t.status !== 'resolved').length;
            if (open > 0) { badge.textContent = open; badge.classList.remove('hidden'); }
            else badge.classList.add('hidden');
        } catch (err) {
            ticketsArea.innerHTML =
                `<div class="empty"><div class="ico">⚠️</div><div>${escapeHtml(err.message || 'Failed to load tickets')}</div></div>`;
        }
    }

    function render(list) {
        if (!list.length) {
            ticketsArea.innerHTML = '<div class="empty"><div class="ico">🎫</div><div>No tickets yet.</div></div>';
            return;
        }
        const rows = list.map(t => `
            <tr data-id="${t.id}" style="cursor:pointer;">
                <td><div style="font-weight:600;">${escapeHtml(t.agencyName || t.agencySlug)}</div></td>
                <td>${escapeHtml(t.subject)}</td>
                <td>${t.screenshotUrl ? '📎' : ''}</td>
                <td>${statusPill(t.status)}</td>
                <td class="text-muted" style="font-size:12px;">${escapeHtml(t.createdAt)}</td>
                <td>${t.adminReply ? '<span class="text-muted" style="font-size:12px;">replied</span>' : '<span style="color:#dc2626;font-size:12px;font-weight:600;">needs reply</span>'}</td>
            </tr>`).join('');
        ticketsArea.innerHTML = `
            <table class="data-table">
                <thead><tr>
                    <th>Agency</th><th>Subject</th><th></th><th>Status</th><th>Reported</th><th></th>
                </tr></thead>
                <tbody>${rows}</tbody>
            </table>`;
        ticketsArea.querySelectorAll('tr[data-id]').forEach(tr =>
            tr.addEventListener('click', () => openTicket(+tr.dataset.id)));
    }

    function openTicket(id) {
        const t = _cache.find(x => x.id === id);
        if (!t) return;
        _open = t;
        tkTitle.textContent = t.subject;
        tkMeta.innerHTML = `${escapeHtml(t.agencyName || t.agencySlug)} • reported ${escapeHtml(t.createdAt)}`;
        tkMsg.textContent = t.message;
        if (t.screenshotUrl) {
            tkShot.src = t.screenshotUrl; tkShotL.href = t.screenshotUrl;
            tkShotW.classList.remove('hidden');
        } else tkShotW.classList.add('hidden');
        tkStat.value = t.status || 'open';
        tkReply.value = t.adminReply || '';
        modal.classList.add('is-open');
    }

    function closeModal() { modal.classList.remove('is-open'); _open = null; }
    tkCancel.addEventListener('click', closeModal);
    modal.addEventListener('click', e => { if (e.target === modal) closeModal(); });

    tkSave.addEventListener('click', async () => {
        if (!_open) return;
        tkSave.disabled = true;
        try {
            await api(`/manage/tickets/${_open.id}`, {
                method: 'POST',
                body: { status: tkStat.value, adminReply: tkReply.value }
            });
            toast('Ticket updated', 'success');
            closeModal();
            await loadTickets();
        } catch (err) {
            toast(err.message || 'Failed to save', 'error');
        } finally { tkSave.disabled = false; }
    });
})();
