// Register flow: per-email OTP verification (independent), then final submit.
// Both email fields can be verified independently; secondary is optional.

const state = {
    email1Verified: false,
    email2Verified: false,
    otp: { email: null, slot: null }, // slot = 1 or 2 (which field is being verified)
    logoFile: null,                   // compressed Blob ready to upload
};

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

// ── Email verify buttons ──
const email1Input = document.getElementById('email1');
const email2Input = document.getElementById('email2');
const verify1Btn  = document.getElementById('verify-email1');
const verify2Btn  = document.getElementById('verify-email2');
const email1Help  = document.getElementById('email1-help');
const email2Help  = document.getElementById('email2-help');

// Enable email2 verify only when a value is typed
email2Input.addEventListener('input', () => {
    verify2Btn.disabled = !email2Input.value.trim();
    if (state.email2Verified && email2Input.value.trim() !== state.email2VerifiedValue) {
        state.email2Verified = false;
        email2Help.innerHTML = 'Optional. If provided, must also be verified.';
    }
});
email1Input.addEventListener('input', () => {
    if (state.email1Verified && email1Input.value.trim() !== state.email1VerifiedValue) {
        state.email1Verified = false;
        email1Help.innerHTML = 'A 6-digit code will be sent for verification.';
    }
});

verify1Btn.addEventListener('click', () => startVerify(1));
verify2Btn.addEventListener('click', () => startVerify(2));

async function startVerify(slot) {
    const input = slot === 1 ? email1Input : email2Input;
    const email = input.value.trim();
    if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
        toast('Please enter a valid email address.', 'error');
        return;
    }
    const btn = slot === 1 ? verify1Btn : verify2Btn;
    const orig = btn.innerHTML;
    btn.disabled = true;
    btn.innerHTML = '<span class="spinner"></span>';
    try {
        await api('/otp/send', { method: 'POST', body: { email } });
        toast(`Verification code sent to ${email}`, 'success');
        openOtpModal(email, slot);
    } catch (e) {
        toast(e.message || 'Failed to send code', 'error');
    } finally {
        btn.disabled = false;
        btn.innerHTML = orig;
    }
}

// ── OTP modal ──
const otpModal   = document.getElementById('otp-modal');
const otpEmail   = document.getElementById('otp-email');
const otpInputs  = document.querySelectorAll('#otp-input input');
const otpError   = document.getElementById('otp-error');
const otpConfirm = document.getElementById('otp-confirm');
const otpCancel  = document.getElementById('otp-cancel');
const otpResend  = document.getElementById('otp-resend');

function openOtpModal(email, slot) {
    state.otp.email = email; state.otp.slot = slot;
    otpEmail.textContent = email;
    otpError.classList.add('hidden'); otpError.textContent = '';
    otpInputs.forEach(i => i.value = '');
    otpModal.classList.add('is-open');
    setTimeout(() => otpInputs[0].focus(), 50);
}
function closeOtpModal() { otpModal.classList.remove('is-open'); }
otpCancel.addEventListener('click', closeOtpModal);

// Auto-advance + paste handling in OTP boxes
otpInputs.forEach((inp, idx) => {
    inp.addEventListener('input', (e) => {
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
    try {
        await api('/otp/send', { method: 'POST', body: { email: state.otp.email } });
        toast('New code sent', 'success');
    } catch (e) { toast(e.message || 'Failed to resend', 'error'); }
});

otpConfirm.addEventListener('click', async () => {
    const code = Array.from(otpInputs).map(i => i.value).join('');
    if (code.length !== 6) { otpError.textContent = 'Enter all 6 digits.'; otpError.classList.remove('hidden'); return; }
    otpError.classList.add('hidden');
    otpConfirm.disabled = true; otpConfirm.innerHTML = '<span class="spinner"></span>';
    try {
        await api('/otp/verify', { method: 'POST', body: { email: state.otp.email, code } });
        // Success
        const slot = state.otp.slot;
        if (slot === 1) {
            state.email1Verified = true;
            state.email1VerifiedValue = state.otp.email;
            email1Input.readOnly = true;
            email1Help.innerHTML = '<span class="badge badge-verified">Verified</span>';
        } else {
            state.email2Verified = true;
            state.email2VerifiedValue = state.otp.email;
            email2Input.readOnly = true;
            email2Help.innerHTML = '<span class="badge badge-verified">Verified</span>';
        }
        toast('Email verified', 'success');
        closeOtpModal();
    } catch (e) {
        otpError.textContent = e.message || 'Invalid code'; otpError.classList.remove('hidden');
    } finally {
        otpConfirm.disabled = false; otpConfirm.innerHTML = 'Verify';
    }
});

// ── Submit ──
const form       = document.getElementById('register-form');
const submitBtn  = document.getElementById('submit-btn');
const formCard   = document.getElementById('form-card');
const successCard= document.getElementById('success-card');
const successEml = document.getElementById('success-email');

form.addEventListener('submit', async (e) => {
    e.preventDefault();
    const name     = document.getElementById('name').value.trim();
    const mobile1  = document.getElementById('mobile1').value.trim();
    const mobile2  = document.getElementById('mobile2').value.trim();
    const address  = document.getElementById('address').value.trim();
    const email1   = email1Input.value.trim();
    const email2   = email2Input.value.trim();
    const password = document.getElementById('password').value;
    const password2= document.getElementById('password2').value;

    if (!name || !mobile1 || !address || !email1 || !password) {
        toast('Please fill all required fields.', 'error'); return;
    }
    if (!state.email1Verified) {
        toast('Please verify the primary email.', 'error'); return;
    }
    if (email2 && !state.email2Verified) {
        toast('Please verify the secondary email or remove it.', 'error'); return;
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
