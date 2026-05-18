// Shared portal helpers — same-origin (`/api/agency/...`).

const API_BASE = '/api/agency';

async function api(path, opts = {}) {
    const init = {
        method: 'GET',
        headers: { 'Accept': 'application/json' },
        ...opts,
    };
    if (init.body && !(init.body instanceof FormData) && typeof init.body !== 'string') {
        init.headers['Content-Type'] = 'application/json';
        init.body = JSON.stringify(init.body);
    }
    const token = sessionStorage.getItem('manage_token');
    if (token) init.headers['X-Manage-Token'] = token;

    const res = await fetch(API_BASE + path, init);
    const text = await res.text();
    let data; try { data = text ? JSON.parse(text) : null; } catch { data = { raw: text }; }
    if (!res.ok) {
        const err = new Error((data && data.message) || ('HTTP ' + res.status));
        err.status = res.status; err.body = data;
        throw err;
    }
    return data;
}

function toast(msg, type = 'info', ttl = 4000) {
    const wrap = document.getElementById('toasts');
    if (!wrap) { console.log('[' + type + '] ' + msg); return; }
    const el = document.createElement('div');
    el.className = 'toast ' + type;
    el.textContent = msg;
    wrap.appendChild(el);
    setTimeout(() => { el.style.opacity = '0'; setTimeout(() => el.remove(), 200); }, ttl);
}

// Compress an image File on-device to a target max dimension + jpeg quality.
// Returns a Blob (image/jpeg). Browsers do this client-side so the server
// never receives a huge original.
async function compressImage(file, maxDim = 512, quality = 0.85) {
    if (!file || !file.type.startsWith('image/')) return file;
    const imgUrl = URL.createObjectURL(file);
    try {
        const img = await new Promise((res, rej) => {
            const i = new Image();
            i.onload = () => res(i);
            i.onerror = rej;
            i.src = imgUrl;
        });
        let { width, height } = img;
        if (width > maxDim || height > maxDim) {
            const r = Math.min(maxDim / width, maxDim / height);
            width = Math.round(width * r);
            height = Math.round(height * r);
        }
        const canvas = document.createElement('canvas');
        canvas.width = width; canvas.height = height;
        const ctx = canvas.getContext('2d');
        // White backing so transparent PNG → JPEG doesn't go black
        ctx.fillStyle = '#FFFFFF';
        ctx.fillRect(0, 0, width, height);
        ctx.drawImage(img, 0, 0, width, height);
        return await new Promise(r => canvas.toBlob(r, 'image/jpeg', quality));
    } finally {
        URL.revokeObjectURL(imgUrl);
    }
}

function formatDate(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (isNaN(d)) return iso;
    return d.toLocaleString();
}
function escapeHtml(s) {
    return String(s == null ? '' : s)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
