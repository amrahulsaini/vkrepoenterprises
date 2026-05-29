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
    const tkThread= document.getElementById('tk-thread');
    const tkStat  = document.getElementById('tk-status');
    const tkReply = document.getElementById('tk-reply');
    const tkSend  = document.getElementById('tk-send');
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
                <td><span style="color:#f5a623;font-size:12px;font-weight:600;">Open conversation ›</span></td>
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

    async function openTicket(id) {
        const t = _cache.find(x => x.id === id);
        if (!t) return;
        _open = t;
        tkTitle.textContent = t.subject;
        tkMeta.innerHTML = `${escapeHtml(t.agencyName || t.agencySlug)} • reported ${escapeHtml(t.createdAt)}`;
        tkStat.value = t.status || 'open';
        tkReply.value = '';
        modal.classList.add('is-open');
        await renderThread(t);
    }

    // Builds the conversation: opening message (+screenshot), then each
    // back-and-forth message bubble with sender + time.
    async function renderThread(t) {
        tkThread.innerHTML = '<div class="text-muted" style="font-size:12px;">Loading…</div>';
        let msgs = [];
        try { msgs = await api(`/manage/tickets/${t.id}/messages`) || []; }
        catch { /* show at least the opening message */ }

        const bubble = (who, body, when, mine) => `
            <div style="display:flex; ${mine ? 'justify-content:flex-end;' : ''} margin-bottom:8px;">
              <div style="max-width:78%; background:${mine ? '#eff6ff' : '#fff'}; border:1px solid #e5e7eb; border-radius:10px; padding:9px 12px;">
                <div style="font-size:10px; font-weight:700; color:${mine ? '#1565c0' : '#c05a00'};">${escapeHtml(who)}</div>
                <div style="font-size:13px; white-space:pre-wrap; color:#1f2937; margin-top:2px;">${escapeHtml(body)}</div>
                <div style="font-size:9px; color:#94a3b8; text-align:right; margin-top:3px;">${escapeHtml(when)}</div>
              </div>
            </div>`;

        let html = bubble(t.agencyName || 'Agency', t.message, t.createdAt, false);
        if (t.screenshotUrl)
            html += `<div style="margin:0 0 8px;"><a href="${t.screenshotUrl}" target="_blank"><img src="${t.screenshotUrl}" style="max-width:60%; max-height:180px; border-radius:8px; border:1px solid #e5e7eb;"/></a></div>`;
        for (const m of msgs)
            html += bubble(m.sender === 'admin' ? 'CRMRS (you)' : (t.agencyName || 'Agency'), m.body, m.createdAt, m.sender === 'admin');
        tkThread.innerHTML = html;
        tkThread.scrollTop = tkThread.scrollHeight;
    }

    function closeModal() { modal.classList.remove('is-open'); _open = null; }
    tkCancel.addEventListener('click', closeModal);
    modal.addEventListener('click', e => { if (e.target === modal) closeModal(); });

    // Status change → save immediately (separate from messages).
    tkStat.addEventListener('change', async () => {
        if (!_open) return;
        try { await api(`/manage/tickets/${_open.id}`, { method: 'POST', body: { status: tkStat.value } }); toast('Status updated', 'success'); _open.status = tkStat.value; }
        catch (err) { toast(err.message || 'Failed', 'error'); }
    });

    // Send a message — can be done any number of times.
    tkSend.addEventListener('click', async () => {
        if (!_open) return;
        const body = tkReply.value.trim();
        if (!body) return;
        tkSend.disabled = true;
        try {
            await api(`/manage/tickets/${_open.id}/messages`, { method: 'POST', body: { body } });
            tkReply.value = '';
            await renderThread(_open);
            loadTickets();   // refresh list previews (don't await — keep modal open)
        } catch (err) {
            toast(err.message || 'Failed to send', 'error');
        } finally { tkSend.disabled = false; }
    });
})();
