// Register flow: per-channel OTP verification (email via SMTP, mobile via SMS),
// then final submit. Primary email + mobile 1 must be verified; the secondary
// email and mobile 2 are optional but, if filled, must be verified too.
// Duplicate primary/secondary values are rejected inline the moment they're typed.

// The mobile SMS OTP reuses the app's working MSG91 provision on the mobile API
// (api.crmrecoverysoftware.com/api/mobile) — CORS is open and these endpoints
// run before any login/tenant, so the browser can call them directly.
const MOBILE_API = 'https://api.crmrecoverysoftware.com/api/mobile';
async function mobileApi(path, body) {
    const res = await fetch(MOBILE_API + path, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', 'Accept': 'application/json' },
        body: JSON.stringify(body),
    });
    const text = await res.text();
    let data; try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }
    if (!res.ok) {
        const err = new Error((data && data.message) || ('HTTP ' + res.status));
        err.status = res.status; throw err;
    }
    return data;
}

const state = {
    email1Verified: false,
    email2Verified: false,
    mobile1Verified: false,
    mobile2Verified: false,
    // channel: 'email' | 'sms'; target: the email/mobile being verified; slot: 1 | 2
    otp: { channel: 'email', target: null, slot: null },
    logoFile: null,                   // compressed Blob ready to upload
};

// 10-digit normalized key for comparing / sending mobiles (matches server).
function mobKey(v) {
    let d = (v || '').replace(/\D/g, '');
    if (d.length === 12 && d.startsWith('91')) d = d.slice(2);
    else if (d.length === 11 && d.startsWith('0')) d = d.slice(1);
    return d;
}

// ── Logo file picker + on-the-fly compression ──
const logoInput   = document.getElementById('logo');
const logoLabel   = document.getElementById('logo-label');
const logoName    = document.getElementById('logo-name');
const logoPreview = document.getElementById('logo-preview');
const logoImg     = document.getElementById('logo-img');
const logoMeta    = document.getElementById('logo-meta');
const logoClear   = document.getElementById('logo-clear');

logoInput.addEventListener('change', async (e) => {
    const f = e.target.files && e.target.files[0];
    if (!f) return;
    const origKB = Math.round(f.size / 1024);
    const blob = await compressImage(f, 512, 0.85);
    state.logoFile = blob;
    const compKB = Math.round(blob.size / 1024);
    const url = URL.createObjectURL(blob);
    logoImg.src = url;
    logoName.textContent = f.name;
    logoMeta.textContent = `${f.name} · ${origKB} KB → compressed to ${compKB} KB`;
    logoPreview.classList.remove('hidden');
});

logoClear.addEventListener('click', () => {
    state.logoFile = null;
    logoInput.value = '';
    logoName.textContent = 'No file selected';
    logoPreview.classList.add('hidden');
});

// ── Field refs ──
const email1Input = document.getElementById('email1');
const email2Input = document.getElementById('email2');
const verify1Btn  = document.getElementById('verify-email1');
const verify2Btn  = document.getElementById('verify-email2');
const email1Help  = document.getElementById('email1-help');
const email2Help  = document.getElementById('email2-help');

const mobile1Input = document.getElementById('mobile1');
const mobile2Input = document.getElementById('mobile2');
const vMobile1Btn  = document.getElementById('verify-mobile1');
const vMobile2Btn  = document.getElementById('verify-mobile2');
const mobile1Help  = document.getElementById('mobile1-help');
const mobile2Help  = document.getElementById('mobile2-help');

const VERIFIED_BADGE = '<span class="badge badge-verified">Verified</span>';

function setHelp(el, html, isError) {
    el.innerHTML = html;
    el.classList.toggle('error', !!isError);
}

// ── Instant duplicate validation (primary ≠ secondary) ──
// Returns true when the secondary value clashes with the primary.
function emailsDuplicate() {
    const a = email1Input.value.trim().toLowerCase();
    const b = email2Input.value.trim().toLowerCase();
    return a && b && a === b;
}
function mobilesDuplicate() {
    const a = mobKey(mobile1Input.value);
    const b = mobKey(mobile2Input.value);
    return a && b && a === b;
}

function refreshEmail2State() {
    const has = email2Input.value.trim().length > 0;
    if (has && emailsDuplicate()) {
        state.email2Verified = false;
        verify2Btn.disabled = true;
        setHelp(email2Help, 'Secondary email must be different from the primary email.', true);
        return;
    }
    verify2Btn.disabled = !has || state.email2Verified;
    if (state.email2Verified && email2Input.value.trim() === state.email2VerifiedValue) {
        setHelp(email2Help, VERIFIED_BADGE, false);
    } else {
        state.email2Verified = false;
        setHelp(email2Help, 'Optional. If provided, must also be verified.', false);
    }
}

function refreshMobile2State() {
    const has = mobile2Input.value.trim().length > 0;
    if (has && mobilesDuplicate()) {
        state.mobile2Verified = false;
        vMobile2Btn.disabled = true;
        setHelp(mobile2Help, 'Mobile number 2 must be different from mobile number 1.', true);
        return;
    }
    vMobile2Btn.disabled = !has || state.mobile2Verified;
    if (state.mobile2Verified && mobKey(mobile2Input.value) === state.mobile2VerifiedValue) {
        setHelp(mobile2Help, VERIFIED_BADGE, false);
    } else {
        state.mobile2Verified = false;
        setHelp(mobile2Help, 'Optional. If provided, must also be verified.', false);
    }
}

// Editing a verified field invalidates its verification.
email1Input.addEventListener('input', () => {
    if (state.email1Verified && email1Input.value.trim() !== state.email1VerifiedValue) {
        state.email1Verified = false;
        email1Input.readOnly = false;
        setHelp(email1Help, 'A 6-digit code will be sent for verification.', false);
    }
    refreshEmail2State();
});
email2Input.addEventListener('input', refreshEmail2State);

mobile1Input.addEventListener('input', () => {
    if (state.mobile1Verified && mobKey(mobile1Input.value) !== state.mobile1VerifiedValue) {
        state.mobile1Verified = false;
        mobile1Input.readOnly = false;
        vMobile1Btn.disabled = false;
        setHelp(mobile1Help, 'A 6-digit code will be sent by SMS.', false);
    }
    refreshMobile2State();
});
mobile2Input.addEventListener('input', refreshMobile2State);

// ── Send-OTP buttons ──
verify1Btn.addEventListener('click', () => startEmailVerify(1));
verify2Btn.addEventListener('click', () => startEmailVerify(2));
vMobile1Btn.addEventListener('click', () => startMobileVerify(1));
vMobile2Btn.addEventListener('click', () => startMobileVerify(2));

async function withButtonSpinner(btn, fn) {
    const orig = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="spinner"></span>';
    try { await fn(); }
    finally { btn.disabled = false; btn.innerHTML = orig; }
}

async function startEmailVerify(slot) {
    const input = slot === 1 ? email1Input : email2Input;
    const btn   = slot === 1 ? verify1Btn  : verify2Btn;
    const email = input.value.trim();
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        toast('Please enter a valid email address.', 'error'); return;
    }
    if (slot === 2 && emailsDuplicate()) {
        toast('Secondary email must be different from the primary email.', 'error'); return;
    }
    await withButtonSpinner(btn, async () => {
        try {
            await api('/otp/send', { method: 'POST', body: { email } });
            toast(`Verification code sent to ${email}`, 'success');
            openOtpModal('email', email, slot);
        } catch (e) { toast(e.message || 'Failed to send code', 'error'); }
    });
}

async function startMobileVerify(slot) {
    const input = slot === 1 ? mobile1Input : mobile2Input;
    const btn   = slot === 1 ? vMobile1Btn  : vMobile2Btn;
    const mobile = input.value.trim();
    if (mobKey(mobile).length !== 10) {
        toast('Please enter a valid 10-digit mobile number.', 'error'); return;
    }
    if (slot === 2 && mobilesDuplicate()) {
        toast('Mobile number 2 must be different from mobile number 1.', 'error'); return;
    }
    await withButtonSpinner(btn, async () => {
        try {
            await mobileApi('/otp/send', { mobile });
            toast(`OTP sent by SMS to ${mobile}`, 'success');
            openOtpModal('sms', mobile, slot);
        } catch (e) { toast(e.message || 'Failed to send OTP', 'error'); }
    });
}

// ── OTP modal (shared by email + SMS) ──
const otpModal   = document.getElementById('otp-modal');
const otpTitle   = document.getElementById('otp-title');
const otpDesc    = document.getElementById('otp-desc');
const otpEmail   = document.getElementById('otp-email');
const otpInputs  = document.querySelectorAll('#otp-input input');
const otpError   = document.getElementById('otp-error');
const otpConfirm = document.getElementById('otp-confirm');
const otpCancel  = document.getElementById('otp-cancel');
const otpResend  = document.getElementById('otp-resend');

function openOtpModal(channel, target, slot) {
    state.otp.channel = channel; state.otp.target = target; state.otp.slot = slot;
    otpTitle.textContent = channel === 'sms' ? 'Verify your mobile' : 'Verify your email';
    otpDesc.innerHTML = `Enter the 6-digit code we just sent ${channel === 'sms' ? 'by SMS to' : 'to'} <strong id="otp-email"></strong>.`;
    document.getElementById('otp-email').textContent = target;
    otpError.classList.add('hidden'); otpError.textContent = '';
    otpInputs.forEach(i => i.value = '');
    otpModal.classList.add('is-open');
    setTimeout(() => otpInputs[0].focus(), 50);
}
function closeOtpModal() { otpModal.classList.remove('is-open'); }
otpCancel.addEventListener('click', closeOtpModal);

// Auto-advance + paste handling in OTP boxes
otpInputs.forEach((inp, idx) => {
    inp.addEventListener('input', () => {
        inp.value = inp.value.replace(/\D/g, '').slice(0, 1);
        if (inp.value && idx < otpInputs.length - 1) otpInputs[idx + 1].focus();
        if (idx === otpInputs.length - 1 && Array.from(otpInputs).every(i => i.value)) {
            otpConfirm.click();
        }
    });
    inp.addEventListener('keydown', (e) => {
        if (e.key === 'Backspace' && !inp.value && idx > 0) otpInputs[idx - 1].focus();
    });
    inp.addEventListener('paste', (e) => {
        e.preventDefault();
        const txt = (e.clipboardData.getData('text') || '').replace(/\D/g, '').slice(0, 6);
        if (!txt) return;
        for (let i = 0; i < 6; i++) otpInputs[i].value = txt[i] || '';
        const next = Math.min(txt.length, 5);
        otpInputs[next].focus();
        if (txt.length === 6) otpConfirm.click();
    });
});

otpResend.addEventListener('click', async () => {
    const { channel, target } = state.otp;
    try {
        if (channel === 'sms') await mobileApi('/otp/send', { mobile: target });
        else                   await api('/otp/send', { method: 'POST', body: { email: target } });
        toast('New code sent', 'success');
    } catch (e) { toast(e.message || 'Failed to resend', 'error'); }
});

otpConfirm.addEventListener('click', async () => {
    const code = Array.from(otpInputs).map(i => i.value).join('');
    if (code.length !== 6) { otpError.textContent = 'Enter all 6 digits.'; otpError.classList.remove('hidden'); return; }
    otpError.classList.add('hidden');
    otpConfirm.disabled = true; otpConfirm.innerHTML = '<span class="spinner"></span>';
    const { channel, target, slot } = state.otp;
    try {
        if (channel === 'sms') await mobileApi('/otp/verify', { mobile: target, otp: code });
        else                   await api('/otp/verify', { method: 'POST', body: { email: target, code } });

        if (channel === 'email') {
            if (slot === 1) {
                state.email1Verified = true; state.email1VerifiedValue = target;
                email1Input.readOnly = true; verify1Btn.disabled = true;
                setHelp(email1Help, VERIFIED_BADGE, false);
            } else {
                state.email2Verified = true; state.email2VerifiedValue = target;
                email2Input.readOnly = true; verify2Btn.disabled = true;
                setHelp(email2Help, VERIFIED_BADGE, false);
            }
            toast('Email verified', 'success');
        } else {
            if (slot === 1) {
                state.mobile1Verified = true; state.mobile1VerifiedValue = mobKey(target);
                mobile1Input.readOnly = true; vMobile1Btn.disabled = true;
                setHelp(mobile1Help, VERIFIED_BADGE, false);
            } else {
                state.mobile2Verified = true; state.mobile2VerifiedValue = mobKey(target);
                mobile2Input.readOnly = true; vMobile2Btn.disabled = true;
                setHelp(mobile2Help, VERIFIED_BADGE, false);
            }
            toast('Mobile verified', 'success');
        }
        closeOtpModal();
    } catch (e) {
        otpError.textContent = e.message || 'Invalid code'; otpError.classList.remove('hidden');
    } finally {
        otpConfirm.disabled = false; otpConfirm.innerHTML = 'Verify';
    }
});

// ── Submit ──
const form        = document.getElementById('register-form');
const submitBtn   = document.getElementById('submit-btn');
const formCard    = document.getElementById('form-card');
const successCard = document.getElementById('success-card');
const successEml  = document.getElementById('success-email');

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const name     = document.getElementById('name').value.trim();
    const mobile1  = mobile1Input.value.trim();
    const mobile2  = mobile2Input.value.trim();
    const address  = document.getElementById('address').value.trim();
    const email1   = email1Input.value.trim();
    const email2   = email2Input.value.trim();
    const password = document.getElementById('password').value;
    const password2= document.getElementById('password2').value;

    if (!name || !mobile1 || !address || !email1 || !password) {
        toast('Please fill all required fields.', 'error'); return;
    }
    if (emailsDuplicate()) {
        toast('Primary and secondary emails must be different.', 'error'); return;
    }
    if (mobilesDuplicate()) {
        toast('Mobile number 1 and 2 must be different.', 'error'); return;
    }
    if (!state.email1Verified) {
        toast('Please verify the primary email.', 'error'); return;
    }
    if (email2 && !state.email2Verified) {
        toast('Please verify the secondary email or remove it.', 'error'); return;
    }
    if (!state.mobile1Verified) {
        toast('Please verify mobile number 1.', 'error'); return;
    }
    if (mobile2 && !state.mobile2Verified) {
        toast('Please verify mobile number 2 or remove it.', 'error'); return;
    }
    if (password !== password2) {
        toast('Passwords do not match.', 'error'); return;
    }

    submitBtn.disabled = true;
    submitBtn.innerHTML = '<span class="spinner"></span>&nbsp; Submitting…';
    try {
        const fd = new FormData();
        fd.append('name', name);
        fd.append('mobile1', mobile1);
        if (mobile2) fd.append('mobile2', mobile2);
        fd.append('address', address);
        fd.append('email1', email1);
        if (email2) fd.append('email2', email2);
        fd.append('password', password);
        if (state.logoFile) fd.append('logo', state.logoFile, 'logo.jpg');

        await api('/register', { method: 'POST', body: fd });
        successEml.textContent = email1;
        formCard.classList.add('hidden');
        successCard.classList.remove('hidden');
        window.scrollTo({ top: 0, behavior: 'smooth' });
    } catch (e) {
        toast(e.message || 'Registration failed', 'error');
        submitBtn.disabled = false;
        submitBtn.innerHTML = 'Create my agency account';
    }
});
