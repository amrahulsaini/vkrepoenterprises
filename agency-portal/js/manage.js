// Manage page: password gate → list pending/approved/rejected → approve / reject.

const gate     = document.getElementById('gate');
const panel    = document.getElementById('panel');
const gateForm = document.getElementById('gate-form');
const gatePass = document.getElementById('gate-pass');
const gateErr  = document.getElementById('gate-err');
const logoutBtn= document.getElementById('logout-btn');
const tableArea= document.getElementById('table-area');
const tabs     = document.getElementById('tabs');
const refreshBtn = document.getElementById('refresh-btn');

let currentStatus = 'pending';

// Auto-unlock if a token is already saved
window.addEventListener('DOMContentLoaded', () => {
    if (sessionStorage.getItem('manage_token')) showPanel();
    else gatePass.focus();
});

gateForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    gateErr.classList.add('hidden');
    const pwd = gatePass.value;
    if (!pwd) return;
    try {
        const r = await api('/manage/login', { method: 'POST', body: { password: pwd } });
        sessionStorage.setItem('manage_token', r.token);
        showPanel();
    } catch (e) {
        gateErr.textContent = e.message || 'Incorrect password';
        gateErr.classList.remove('hidden');
        gatePass.select();
    }
});

logoutBtn.addEventListener('click', () => {
    sessionStorage.removeItem('manage_token');
    panel.classList.add('hidden');
    gate.classList.remove('hidden');
    logoutBtn.classList.add('hidden');
    gatePass.value = '';
    gatePass.focus();
});

function showPanel() {
    gate.classList.add('hidden');
    panel.classList.remove('hidden');
    logoutBtn.classList.remove('hidden');
    loadList();
}

tabs.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-status]');
    if (!btn) return;
    tabs.querySelectorAll('button').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    currentStatus = btn.dataset.status;
    loadList();
});

refreshBtn.addEventListener('click', loadList);

async function loadList() {
    tableArea.innerHTML = `<div class="empty"><div class="ico">⏳</div><div>Loading…</div></div>`;
    try {
        const r = await api('/manage/list?status=' + encodeURIComponent(currentStatus));
        renderTable(r.agencies || []);
    } catch (e) {
        if (e.status === 401) {  // token expired
            sessionStorage.removeItem('manage_token');
            panel.classList.add('hidden'); gate.classList.remove('hidden');
            logoutBtn.classList.add('hidden');
            toast('Session expired — please sign in again.', 'info');
            return;
        }
        tableArea.innerHTML = `<div class="empty"><div class="ico">⚠</div><div>${escapeHtml(e.message || 'Failed to load')}</div></div>`;
    }
}

function renderTable(rows) {
    if (!rows.length) {
        tableArea.innerHTML = `<div class="empty"><div class="ico">📭</div><div>No agencies in <strong>${escapeHtml(currentStatus)}</strong>.</div></div>`;
        return;
    }
    const html = `
        <div class="table-wrap">
        <table class="data">
            <thead>
                <tr>
                    <th>Agency</th>
                    <th>Contact</th>
                    <th>Status</th>
                    <th>Registered</th>
                    <th style="text-align:right;">Actions</th>
                </tr>
            </thead>
            <tbody>
                ${rows.map(r => rowHtml(r)).join('')}
            </tbody>
        </table>
        </div>
    `;
    tableArea.innerHTML = html;
    tableArea.querySelectorAll('[data-act]').forEach(btn => {
        btn.addEventListener('click', () => handleAction(btn.dataset.act, +btn.dataset.id));
    });
}

function rowHtml(a) {
    const status =
        a.status === 'approved' ? 'badge-approved' :
        a.status === 'rejected' ? 'badge-rejected' :
        'badge-pending';
    const logo = a.logoPath ? `<img src="${escapeHtml(a.logoPath)}" alt="">` : `<img src="assets/logo-short.webp" alt="" style="opacity:.4">`;
    const actions = a.status === 'pending'
        ? `<button class="btn btn-success btn-sm" data-act="approve" data-id="${a.id}">Approve</button>
           <button class="btn btn-outline btn-sm" data-act="reject"  data-id="${a.id}">Reject</button>`
        : a.status === 'approved'
            ? `<span class="text-muted">✓ Active</span>`
            : `<span class="text-muted" title="${escapeHtml(a.rejectedReason || '')}">Reason: ${escapeHtml(a.rejectedReason || '—')}</span>`;
    return `
        <tr>
            <td>
                <div class="agency-cell">
                    ${logo}
                    <div>
                        <div class="name">${escapeHtml(a.name)}</div>
                        <div class="muted">${escapeHtml(a.address || '')}</div>
                    </div>
                </div>
            </td>
            <td>
                <div>${escapeHtml(a.email1)}</div>
                <div class="muted">${escapeHtml(a.mobile1)}${a.mobile2 ? ' · ' + escapeHtml(a.mobile2) : ''}</div>
            </td>
            <td><span class="badge ${status}">${escapeHtml(a.status)}</span></td>
            <td><div class="muted">${escapeHtml(formatDate(a.createdAt))}</div></td>
            <td><div class="row-actions">${actions}</div></td>
        </tr>
    `;
}

// ── Approve / Reject ──
const rejectModal   = document.getElementById('reject-modal');
const rejectReason  = document.getElementById('reject-reason');
const rejectConfirm = document.getElementById('reject-confirm');
const rejectCancel  = document.getElementById('reject-cancel');
let pendingRejectId = null;

rejectCancel.addEventListener('click', () => rejectModal.classList.remove('is-open'));
rejectConfirm.addEventListener('click', async () => {
    if (pendingRejectId == null) return;
    rejectConfirm.disabled = true; rejectConfirm.innerHTML = '<span class="spinner"></span>';
    try {
        await api(`/manage/reject/${pendingRejectId}`, { method: 'POST', body: { reason: rejectReason.value || '' } });
        toast('Agency rejected', 'info');
        rejectModal.classList.remove('is-open');
        rejectReason.value = '';
        loadList();
    } catch (e) {
        toast(e.message || 'Failed to reject', 'error');
    } finally {
        rejectConfirm.disabled = false; rejectConfirm.innerHTML = 'Reject';
    }
});

async function handleAction(act, id) {
    if (act === 'reject') {
        pendingRejectId = id;
        rejectReason.value = '';
        rejectModal.classList.add('is-open');
        return;
    }
    if (act === 'approve') {
        if (!confirm('Approve this agency? They will be activated and emailed their sign-in details.')) return;
        try {
            await api(`/manage/approve/${id}`, { method: 'POST' });
            toast('Agency approved & activated', 'success', 4000);
            loadList();
        } catch (e) {
            toast(e.message || 'Approval failed', 'error', 6000);
        }
    }
}
