// Two-step admin gate: password → OTP emailed to the administrator → token.
// After the token is in sessionStorage the page works the same as before
// (list / approve / reject pending agencies).

// ── Gate elements ──────────────────────────────────────────────────────
const gate         = document.getElementById('gate');
const panel        = document.getElementById('panel');
const logoutBtn    = document.getElementById('logout-btn');
const tableArea    = document.getElementById('table-area');
const tabs         = document.getElementById('tabs');
const refreshBtn   = document.getElementById('refresh-btn');

const step1       = document.getElementById('gate-step1');
const step2       = document.getElementById('gate-step2');
const gateForm    = document.getElementById('gate-form');
const gatePass    = document.getElementById('gate-pass');
const gateErr     = document.getElementById('gate-err');
const gateContinue = document.getElementById('gate-continue');

const otpInputs   = document.querySelectorAll('#gate-otp-input input');
const otpErr      = document.getElementById('gate-otp-err');
const otpBack     = document.getElementById('gate-otp-back');
const otpResend   = document.getElementById('gate-otp-resend');
const otpVerify   = document.getElementById('gate-otp-verify');

let currentStatus = 'pending';
let pendingPassword = '';   // kept in memory between step1 and step2 only

// ── Boot ───────────────────────────────────────────────────────────────
window.addEventListener('DOMContentLoaded', () => {
    if (sessionStorage.getItem('manage_token')) showPanel();
    else gatePass.focus();
});

// ── Sign in — password → token (one step).
// The OTP step is bypassed while SMTP is unconfigured; once SMTP credentials
// are in /opt/vkapi/db/.env.local we can switch back to /manage/otp/request +
// /manage/otp/verify (the two-step flow's UI is still present below).
gateForm.addEventListener('submit', async (e) => {
    e.preventDefault();
    gateErr.classList.add('hidden');
    const pwd = gatePass.value;
    if (!pwd) return;
    gateContinue.disabled = true;
    gateContinue.innerHTML = '<span class="spinner"></span> Signing in…';
    try {
        const r = await api('/manage/login', { method: 'POST', body: { password: pwd } });
        sessionStorage.setItem('manage_token', r.token);
        showPanel();
    } catch (err) {
        gateErr.textContent = err.message || 'Incorrect password.';
        gateErr.classList.remove('hidden');
        gatePass.select();
    } finally {
        gateContinue.disabled = false;
        gateContinue.textContent = 'Sign in';
    }
});

// ── Step 2 — OTP input wiring ──────────────────────────────────────────
otpInputs.forEach((input, idx) => {
    input.addEventListener('input', () => {
        const v = input.value.replace(/\D/g, '');
        input.value = v;
        if (v && idx < otpInputs.length - 1) otpInputs[idx + 1].focus();
    });
    input.addEventListener('keydown', (e) => {
        if (e.key === 'Backspace' && !input.value && idx > 0) otpInputs[idx - 1].focus();
        if (e.key === 'Enter') otpVerify.click();
    });
    input.addEventListener('paste', (e) => {
        const txt = (e.clipboardData || window.clipboardData).getData('text').replace(/\D/g, '').slice(0, 6);
        if (!txt) return;
        e.preventDefault();
        for (let i = 0; i < txt.length && i < otpInputs.length; i++) otpInputs[i].value = txt[i];
        const next = Math.min(txt.length, otpInputs.length - 1);
        otpInputs[next].focus();
    });
});

otpBack.addEventListener('click', () => {
    showStep1();
    gatePass.focus();
});

otpResend.addEventListener('click', async () => {
    if (!pendingPassword) { showStep1(); return; }
    otpErr.classList.add('hidden');
    otpResend.disabled = true;
    otpResend.innerHTML = '<span class="spinner"></span>';
    try {
        await api('/manage/otp/request', { method: 'POST', body: { password: pendingPassword } });
        toast('Code resent', 'success');
        clearOtp(); otpInputs[0].focus();
    } catch (err) {
        toast(err.message || 'Could not resend code', 'error');
    } finally {
        otpResend.disabled = false;
        otpResend.textContent = 'Resend';
    }
});

otpVerify.addEventListener('click', async () => {
    otpErr.classList.add('hidden');
    const code = Array.from(otpInputs).map(i => i.value).join('');
    if (code.length !== 6) {
        otpErr.textContent = 'Enter the 6-digit code.';
        otpErr.classList.remove('hidden');
        return;
    }
    otpVerify.disabled = true;
    otpVerify.innerHTML = '<span class="spinner"></span> Verifying…';
    try {
        const r = await api('/manage/otp/verify', { method: 'POST', body: { code } });
        sessionStorage.setItem('manage_token', r.token);
        pendingPassword = '';
        showPanel();
    } catch (err) {
        otpErr.textContent = err.message || 'Invalid or expired code.';
        otpErr.classList.remove('hidden');
        clearOtp(); otpInputs[0].focus();
    } finally {
        otpVerify.disabled = false;
        otpVerify.textContent = 'Verify & sign in';
    }
});

function showStep1() {
    step2.classList.add('hidden');
    step1.classList.remove('hidden');
    clearOtp();
}

function showStep2() {
    step1.classList.add('hidden');
    step2.classList.remove('hidden');
    setTimeout(() => otpInputs[0].focus(), 50);
}

function clearOtp() {
    otpInputs.forEach(i => i.value = '');
    otpErr.classList.add('hidden');
}

// ── Logout ─────────────────────────────────────────────────────────────
logoutBtn.addEventListener('click', () => {
    sessionStorage.removeItem('manage_token');
    panel.classList.add('hidden');
    gate.classList.remove('hidden');
    logoutBtn.classList.add('hidden');
    showStep1();
    gatePass.value = '';
    gatePass.focus();
});

function showPanel() {
    gate.classList.add('hidden');
    panel.classList.remove('hidden');
    logoutBtn.classList.remove('hidden');
    loadList();
}

// ── Tabs / refresh ─────────────────────────────────────────────────────
tabs.addEventListener('click', (e) => {
    const btn = e.target.closest('button[data-status]');
    if (!btn) return;
    tabs.querySelectorAll('button').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');
    currentStatus = btn.dataset.status;
    loadList();
});
refreshBtn.addEventListener('click', loadList);

// ── Agency list ────────────────────────────────────────────────────────
async function loadList() {
    tableArea.innerHTML = `<div class="empty"><div class="ico">⏳</div><div>Loading…</div></div>`;
    try {
        const r = await api('/manage/list?status=' + encodeURIComponent(currentStatus));
        renderTable(r.agencies || []);
    } catch (e) {
        if (e.status === 401) {
            sessionStorage.removeItem('manage_token');
            panel.classList.add('hidden'); gate.classList.remove('hidden');
            logoutBtn.classList.add('hidden');
            showStep1();
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
            <tbody>${rows.map(r => rowHtml(r)).join('')}</tbody>
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
    // logoPath comes from the DB as e.g. "/agency-uploads/rk_enterprises.jpg".
    // Static files live on the API host, not this static portal host, so we
    // prepend api.crmrecoverysoftware.com explicitly. Absolute URLs pass through
    // unchanged so old data still works.
    const logoSrc = a.logoPath
        ? (/^https?:\/\//i.test(a.logoPath)
            ? a.logoPath
            : 'https://api.crmrecoverysoftware.com' + (a.logoPath.startsWith('/') ? '' : '/') + a.logoPath)
        : 'assets/crmrs-logo.webp';
    const logo = a.logoPath
        ? `<img src="${escapeHtml(logoSrc)}" alt="" onerror="this.src='assets/crmrs-logo.webp'; this.style.opacity='.4';">`
        : `<img src="assets/crmrs-logo.webp" alt="" style="opacity:.4">`;
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

// ── Approve / Reject ───────────────────────────────────────────────────
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
