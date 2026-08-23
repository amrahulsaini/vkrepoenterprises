(function () {
  'use strict';
  var API  = 'https://api.crmrecoverysoftware.com/api/agency';
  var slug = decodeURIComponent((location.pathname.match(/\/hrms\/([^\/]+)/) || [])[1] || '');
  var KEY  = 'crmrs_hrms_' + slug;

  var $ = function (id) { return document.getElementById(id); };
  function show(el, on) { el.classList.toggle('hidden', !on); }
  function say(text, kind) {
    var m = $('auth-msg');
    m.textContent = text || '';
    m.className = 'msg' + (kind ? ' ' + kind : '');
  }
  function busy(btn, on, label) {
    btn.disabled = on;
    if (on) { btn.dataset.html = btn.innerHTML; btn.innerHTML = '<span class="spin"></span>' + (label || ''); }
    else if (btn.dataset.html) { btn.innerHTML = btn.dataset.html; }
  }
  function token(v) {
    if (v === undefined) { try { return localStorage.getItem(KEY) || ''; } catch (e) { return ''; } }
    try { v ? localStorage.setItem(KEY, v) : localStorage.removeItem(KEY); } catch (e) {}
  }

  async function call(path, opts) {
    opts = opts || {};
    var headers = { 'Content-Type': 'application/json' };
    var t = token();
    if (t) headers['X-Hrms-Token'] = t;
    var r = await fetch(API + path, {
      method: opts.method || 'GET',
      headers: headers,
      body: opts.body ? JSON.stringify(opts.body) : undefined
    });
    var data = {};
    try { data = await r.json(); } catch (e) {}
    if (!r.ok) {
      var err = new Error(data.message || ('Request failed (' + r.status + ')'));
      err.code = data.code || '';
      err.status = r.status;
      err.agencyName = data.agencyName || '';
      err.retryAfter = data.retryAfter || 0;
      throw err;
    }
    return data;
  }

  var ME = null, tick = null;

  function logoUrl(p) {
    if (!p) return '/assets/crmrs-logo.webp';
    if (/^https?:\/\//i.test(p)) return p;
    return 'https://api.crmrecoverysoftware.com' + (p.charAt(0) === '/' ? '' : '/') + p;
  }

  function esc(v) {
    return String(v == null ? '' : v).replace(/[&<>"']/g, function (c) {
      return { '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;' }[c];
    });
  }

  function row(k, v) {
    if (!v) return '';
    return '<div class="kv"><div class="k">' + esc(k) + '</div><div class="v">' + esc(v) + '</div></div>';
  }

  function renderCountdown() {
    if (!ME || !ME.sessionExpiresAt) return;
    var ms = new Date(ME.sessionExpiresAt).getTime() - Date.now();
    if (ms <= 0) {
      $('dr-left').textContent = 'Expired';
      $('dr-exp').textContent = 'Sign in again to continue.';
      return;
    }
    var mins = Math.floor(ms / 60000), h = Math.floor(mins / 60), m = mins % 60;
    $('dr-left').textContent = (h > 0 ? h + 'h ' : '') + m + 'm left';
    $('dr-exp').textContent = 'Expires ' + new Date(ME.sessionExpiresAt)
      .toLocaleString([], { day:'2-digit', month:'short', hour:'2-digit', minute:'2-digit' }) +
      ' · ' + (ME.sessionHours || 12) + '-hour session';
  }

  function fillDrawer() {
    if (!ME) return;
    $('dr-logo').src  = logoUrl(ME.logoPath);
    $('dr-name').textContent = ME.agencyName || '';
    $('dr-slug').textContent = ME.slug || '';
    $('dr-rows').innerHTML =
      row('Status', ME.status) +
      row('Primary email', ME.email) +
      row('Secondary email', ME.email2) +
      row('Primary mobile', ME.mobile1) +
      row('Secondary mobile', ME.mobile2) +
      row('Address', ME.address) +
      row('Registered', ME.registeredAt) +
      row('Approved', ME.approvedAt) +
      row('HRMS enabled since', ME.hrmsSince);
    renderCountdown();
  }

  function openDrawer(on) {
    $('drawer').classList.toggle('on', on);
    $('scrim').classList.toggle('on', on);
    if (on) renderCountdown();
  }

  function enterApp(info) {
    ME = info;
    show($('auth'), false);
    show($('app'), true);
    show($('logout'), true);
    show($('chip'), true);
    $('chip-name').textContent = info.agencyName || '';
    $('chip-logo').src = logoUrl(info.logoPath);
    document.title = (info.agencyName ? info.agencyName + ' — ' : '') + 'HRMS — CRMRS';
    fillDrawer();
    if (tick) clearInterval(tick);
    tick = setInterval(renderCountdown, 30000);
  }

  var LABELS = {
    dashboard:'Dashboard', departments:'Departments',
    leave:'Leave', payroll:'Payroll', documents:'Documents', roles:'Roles',
    reports:'Reports', settings:'Settings'
  };

  Array.prototype.forEach.call(document.querySelectorAll('.nav'), function (b) {
    b.addEventListener('click', function () {
      if (b.disabled) return;
      Array.prototype.forEach.call(document.querySelectorAll('.nav'), function (o) {
        o.classList.remove('on');
      });
      b.classList.add('on');
      var page = b.getAttribute('data-page');
      show($('page-profiles'), page === 'profiles');
      show($('page-desktop'), false);
      show($('page-attendance'), page === 'attendance');
      show($('page-roles'), page === 'roles');
      show($('page-roleedit'), false);
      show($('page-stub'), page !== 'profiles' && page !== 'attendance' && page !== 'roles');
      if (page === 'roles') {
        loadRoles();
      } else if (page === 'attendance') {
        if (!$('at-date').value) $('at-date').value = istToday();
        loadAttendance();
      } else if (page !== 'profiles') {
        $('stub-title').textContent = LABELS[page] || page;
      }
    });
  });


  var PROFILES = [], PFID = null;

  function initials(n) {
    var p = (n || '?').trim().split(/\s+/);
    return ((p[0] || '?')[0] + (p.length > 1 ? p[p.length - 1][0] : '')).toUpperCase();
  }

  function avatar(u, big) {
    var ini = esc(initials(u.name));
    var style = big ? ' style="width:44px;height:44px;font-size:15px;border-radius:10px"' : '';
    var img = u.pfpUrl ? '<img src="' + esc(u.pfpUrl) + '" alt="">' : '';
    return '<div class="av"' + style + '>' + ini + img + '</div>';
  }

  function pill(cls, text) { return '<span class="pill ' + cls + '">' + esc(text) + '</span>'; }

  function renderProfiles() {
    var q = ($('pf-q').value || '').trim().toLowerCase();
    var rows = PROFILES.filter(function (u) {
      if (!q) return true;
      return (u.name || '').toLowerCase().indexOf(q) >= 0 ||
             (u.mobile || '').toLowerCase().indexOf(q) >= 0;
    });
    $('pf-count').textContent = rows.length === PROFILES.length
      ? PROFILES.length + ' staff'
      : rows.length + ' of ' + PROFILES.length;

    if (!rows.length) {
      $('pf-rows').innerHTML = '<div class="empty-note" style="border:0">No staff match that search.</div>';
      return;
    }

    $('pf-rows').innerHTML = rows.map(function (u) {
      var status = u.isBlacklisted ? pill('p-red', 'Blacklisted')
                 : !u.isActive     ? pill('p-off', 'Inactive')
                 : pill('p-on', 'Active');
      var kyc = u.kycStatus ? pill(u.kycStatus === 'verified' ? 'p-on' : 'p-off', u.kycStatus) : pill('p-off', 'None');
      var login = u.fingerprintRequired ? pill('p-on', 'Fingerprint')
                : (u.hasPassword ? pill('p-on', 'Password') : pill('p-off', 'Not set'));
      var roleCell = u.roleName ? pill('p-on', u.roleName) : pill('p-off', 'No role');
      return '<div class="trow" data-id="' + u.id + '">' +
        '<div class="who2">' + avatar(u, false) +
        '<div style="min-width:0"><div class="n">' + esc(u.name || 'Unnamed') + '</div>' +
        '<div class="m">' + esc(u.mobile || '') + '</div></div></div>' +
        '<div>' + login + '</div><div>' + roleCell + '</div><div>' + status + '</div></div>';
    }).join('');

    Array.prototype.forEach.call($('pf-rows').querySelectorAll('.trow'), function (r) {
      r.addEventListener('click', function () { openProfile(r.getAttribute('data-id')); });
    });
  }

  async function loadProfiles() {
    $('pf-rows').innerHTML = '<div class="empty-note" style="border:0">Loading…</div>';
    try {
      PROFILES = await call('/hrms/profiles');
      renderProfiles();
    } catch (e) {
      $('pf-rows').innerHTML = '<div class="empty-note" style="border:0">' + esc(e.message) + '</div>';
    }
  }

  function openPfDrawer(on) {
    $('pfdrawer').classList.toggle('on', on);
    $('scrim2').classList.toggle('on', on);
  }

  async function openProfile(id) {
    PFID = id;
    $('pf-pw').value = '';
    $('pf-msg').textContent = '';
    fpMsg('');
    $('pf-rows2').innerHTML = '';
    openPfDrawer(true);
    try {
      var u = await call('/hrms/profiles/' + id);
      $('pf-av').innerHTML = esc(initials(u.name)) +
        (u.pfpUrl ? '<img src="' + esc(u.pfpUrl) + '" alt="">' : '');
      $('pf-name').textContent = u.name || 'Unnamed';
      $('pf-mobile').textContent = u.mobile || '';
      loadFp(u);
      fillRolePicker(u);
      $('pf-pwstate').textContent = u.hasPassword
        ? 'Set' + (u.passwordSetAt ? ' on ' + u.passwordSetAt : '') + '. Entering a new one replaces it.'
        : 'Not set. This person cannot open a desktop mode until you set one.';
      show($('pf-clear'), !!u.hasPassword);
      $('pf-rows2').innerHTML =
        row('Status', u.isBlacklisted ? 'Blacklisted' : (u.isActive ? 'Active' : 'Inactive')) +
        row('Role', u.isAdmin ? 'Admin' : 'Staff') +
        row('Address', u.address) + row('Pincode', u.pincode) +
        row('KYC status', u.kycStatus) + row('KYC name', u.kycName) +
        row('Aadhaar (last 4)', u.kycAadhaarLast4) + row('PAN', u.kycPan) +
        row('Bank holder', u.kycBankHolder) + row('Account no', u.accountNumber) +
        row('IFSC', u.ifsc) + row('Balance', u.balance) +
        row('Registered at', u.regLocation) + row('Joined', u.createdAt) +
        row('Last seen', u.lastSeen) + row('Device linked', u.hasDevice ? 'Yes' : 'No');
    } catch (e) {
      $('pf-rows2').innerHTML = '<div class="empty-note" style="border:0">' + esc(e.message) + '</div>';
    }
  }

  async function savePassword(clear) {
    if (!PFID) return;
    var pw = $('pf-pw').value;
    if (!clear && pw.length < 4) {
      $('pf-msg').textContent = 'Use at least 4 characters.';
      $('pf-msg').className = 'msg err';
      return;
    }
    var btn = clear ? $('pf-clear') : $('pf-save');
    btn.disabled = true;
    try {
      await call('/hrms/profiles/' + PFID + '/password',
        { method: 'POST', body: clear ? { clear: 'true' } : { password: pw } });
      $('pf-msg').textContent = clear ? 'Password removed.' : 'Password set.';
      $('pf-msg').className = 'msg ok';
      $('pf-pw').value = '';
      await loadProfiles();
      await openProfile(PFID);
    } catch (e) {
      $('pf-msg').textContent = e.message;
      $('pf-msg').className = 'msg err';
    } finally { btn.disabled = false; }
  }


  var ATT = null;

  function istToday() {
    var n = new Date();
    var ist = new Date(n.getTime() + (n.getTimezoneOffset() * 60000) + (330 * 60000));
    var m = ist.getMonth() + 1, d = ist.getDate();
    return ist.getFullYear() + '-' + (m < 10 ? '0' : '') + m + '-' + (d < 10 ? '0' : '') + d;
  }

  function renderAttendance() {
    if (!ATT) return;
    $('at-total').textContent = ATT.total;
    $('at-present').textContent = ATT.present;
    $('at-absent').textContent = ATT.absent;
    $('at-when').textContent = ATT.isToday ? 'Today' : ATT.date;

    $('at-rows').innerHTML = ATT.staff.map(function (u) {
      var right = u.marked
        ? '<button class="btn btn-ghost btn-xs" data-un="' + u.id + '">Marked · undo</button>'
        : '<button class="btn btn-accent btn-xs" data-at="' + u.id + '">Mark present</button>';
      var when = u.marked ? esc(u.markedAt) : '<span style="color:var(--muted-2)">&mdash;</span>';
      return '<div class="arow">' +
        '<div class="who2">' + avatar(u, false) +
        '<div style="min-width:0"><div class="n">' + esc(u.name || 'Unnamed') + '</div>' +
        '<div class="m">' + esc(u.mobile || '') + '</div></div></div>' +
        '<div style="font-size:13px;font-variant-numeric:tabular-nums">' + when + '</div>' +
        '<div>' + right + '</div></div>';
    }).join('');

    Array.prototype.forEach.call($('at-rows').querySelectorAll('[data-at]'), function (b) {
      b.addEventListener('click', function () { mark(b.getAttribute('data-at'), true, b); });
    });
    Array.prototype.forEach.call($('at-rows').querySelectorAll('[data-un]'), function (b) {
      b.addEventListener('click', function () { mark(b.getAttribute('data-un'), false, b); });
    });
  }

  async function loadAttendance() {
    var d = $('at-date').value || istToday();
    $('at-rows').innerHTML = '<div class="empty-note" style="border:0">Loading…</div>';
    try {
      ATT = await call('/hrms/attendance?date=' + encodeURIComponent(d));
      $('at-date').value = ATT.date;
      renderAttendance();
    } catch (e) {
      $('at-rows').innerHTML = '<div class="empty-note" style="border:0">' + esc(e.message) + '</div>';
    }
  }

  async function mark(id, on, btn) {
    var d = $('at-date').value || istToday();
    btn.disabled = true;
    try {
      if (on) await call('/hrms/attendance/' + id, { method: 'POST', body: { date: d, status: 'present' } });
      else    await call('/hrms/attendance/' + id + '?date=' + encodeURIComponent(d), { method: 'DELETE' });
      await loadAttendance();
    } catch (e) {
      btn.disabled = false;
      alert(e.message);
    }
  }

  $('at-date').addEventListener('change', loadAttendance);
  $('at-today').addEventListener('click', function () {
    $('at-date').value = istToday();
    loadAttendance();
  });

  $('go-desktop').addEventListener('click', function () {
    show($('page-profiles'), false);
    show($('page-desktop'), true);
    if (!PROFILES.length) loadProfiles();
  });

  $('pf-back').addEventListener('click', function () {
    show($('page-desktop'), false);
    show($('page-profiles'), true);
  });


  var FPREQ = false;

  function fpMsg(t, kind) {
    $('fp-msg').textContent = t || '';
    $('fp-msg').className = 'msg' + (kind ? ' ' + kind : '');
  }

  function renderFp(u, key) {
    FPREQ = !!u.fingerprintRequired;
    $('fp-badge').textContent = FPREQ ? 'Required' : 'Off';
    $('fp-badge').className = 'pill ' + (FPREQ ? 'p-on' : 'p-off');
    $('fp-toggle').textContent = FPREQ ? 'Turn off' : 'Turn on';
    $('fp-toggle').className = 'btn btn-xs ' + (FPREQ ? 'btn-ghost' : 'btn-accent');
    show($('fp-reset'), !!(key && key.enrolled));

    if (key && key.enrolled) {
      $('fp-state').textContent = 'Set up on this person\u2019s phone.';
      $('fp-detail').innerHTML =
        row('Device', key.device || 'Unknown device') +
        row('Key ID', key.keyId) +
        row('Enrolled', key.enrolledAt) +
        row('Last used', key.lastUsedAt);
    } else {
      $('fp-state').textContent = 'Not set up. The staff member turns this on in the CRMRS app on their phone.';
      $('fp-detail').innerHTML = '';
    }
  }

  async function loadFp(u) {
    try {
      var key = await call('/hrms/profiles/' + u.id + '/fingerprint');
      renderFp(u, key);
    } catch (e) {
      renderFp(u, null);
    }
  }

  $('fp-toggle').addEventListener('click', async function () {
    if (!PFID) return;
    var next = !FPREQ;
    $('fp-toggle').disabled = true;
    fpMsg('');
    try {
      await call('/hrms/profiles/' + PFID + '/fingerprint', { method: 'POST', body: { required: next ? 'true' : 'false' } });
      fpMsg(next ? 'Fingerprint is now required.' : 'Fingerprint is no longer required.', 'ok');
      await loadProfiles();
      await openProfile(PFID);
    } catch (e) {
      fpMsg(e.message, 'err');
    } finally { $('fp-toggle').disabled = false; }
  });

  $('fp-reset').addEventListener('click', async function () {
    if (!PFID) return;
    if (!confirm('Reset this fingerprint? They will sign in with their password until they set it up again on a phone.')) return;
    $('fp-reset').disabled = true;
    try {
      await call('/hrms/profiles/' + PFID + '/fingerprint', { method: 'DELETE' });
      fpMsg('Fingerprint reset. Password sign-in is active again.', 'ok');
      await loadProfiles();
      await openProfile(PFID);
    } catch (e) {
      fpMsg(e.message, 'err');
    } finally { $('fp-reset').disabled = false; }
  });


  var MODULES = [], ROLES = [], EDITING = null;

  async function ensureModules() {
    if (MODULES.length) return;
    try { MODULES = await call('/hrms/modules'); } catch (e) { MODULES = []; }
  }

  function roleModsLabel(r) {
    if (r.isSuperadmin) return 'Every module, no restrictions.';
    if (!r.modules || !r.modules.length) return 'No modules selected yet.';
    var names = r.modules.map(function (k) {
      var m = MODULES.filter(function (x) { return x.key === k; })[0];
      return m ? m.label : k;
    });
    if (names.length <= 4) return names.join(', ');
    return names.slice(0, 4).join(', ') + ' +' + (names.length - 4) + ' more';
  }

  async function loadRoles() {
    await ensureModules();
    $('role-list').innerHTML = '<div class="empty-note">Loading\u2026</div>';
    try {
      ROLES = await call('/hrms/roles');
    } catch (e) {
      $('role-list').innerHTML = '<div class="empty-note">' + esc(e.message) + '</div>';
      return;
    }
    $('role-list').innerHTML = ROLES.map(function (r) {
      var badge = r.isSuperadmin ? pill('p-on', 'All access') : pill('p-off', r.modules.length + ' modules');
      var staff = r.staff === 1 ? '1 person' : r.staff + ' people';
      return '<div class="rolecard">' +
        '<div class="rolehead"><div>' +
          '<div class="rolename">' + esc(r.name) + '</div>' +
          '<div class="rolemods">' + esc(roleModsLabel(r)) + '</div>' +
        '</div><div style="display:flex;align-items:center;gap:10px">' +
          badge + '<span class="count">' + staff + '</span>' +
          (r.isSuperadmin ? '' : '<button class="btn btn-ghost btn-xs" data-role="' + r.id + '">Edit</button>') +
        '</div></div></div>';
    }).join('');
    Array.prototype.forEach.call($('role-list').querySelectorAll('[data-role]'), function (b) {
      b.addEventListener('click', function () { openRole(parseInt(b.getAttribute('data-role'), 10)); });
    });
  }

  function renderModulePicker(selected) {
    var groups = [];
    MODULES.forEach(function (m) { if (groups.indexOf(m.group) < 0) groups.push(m.group); });
    $('role-modules').innerHTML = groups.map(function (g) {
      var items = MODULES.filter(function (m) { return m.group === g; }).map(function (m) {
        var on = selected.indexOf(m.key) >= 0 ? ' checked' : '';
        return '<label class="modchk"><input type="checkbox" value="' + esc(m.key) + '"' + on + '>' +
               '<span>' + esc(m.label) + '</span></label>';
      }).join('');
      return '<div class="modgrp">' + esc(g) + '</div><div class="rolegrid">' + items + '</div>';
    }).join('');
    Array.prototype.forEach.call($('role-modules').querySelectorAll('input'), function (c) {
      c.addEventListener('change', countMods);
    });
    countMods();
  }

  function pickedMods() {
    return Array.prototype.slice
      .call($('role-modules').querySelectorAll('input:checked'))
      .map(function (c) { return c.value; });
  }

  function countMods() {
    $('role-count').textContent = pickedMods().length + ' of ' + MODULES.length + ' selected';
  }

  function showRolePage(which) {
    show($('page-roles'), which === 'list');
    show($('page-roleedit'), which === 'edit');
  }

  async function openRole(id) {
    await ensureModules();
    EDITING = id || null;
    var r = ROLES.filter(function (x) { return x.id === id; })[0];
    $('role-title').textContent = r ? 'Edit role' : 'New role';
    $('role-name').value = r ? r.name : '';
    $('role-msg').textContent = '';
    show($('role-del'), !!r);
    renderModulePicker(r ? r.modules.slice() : []);
    showRolePage('edit');
  }

  $('role-new').addEventListener('click', function () { openRole(0); });
  $('role-back').addEventListener('click', function () { showRolePage('list'); loadRoles(); });
  $('role-all').addEventListener('click', function () {
    Array.prototype.forEach.call($('role-modules').querySelectorAll('input'), function (c) { c.checked = true; });
    countMods();
  });
  $('role-none').addEventListener('click', function () {
    Array.prototype.forEach.call($('role-modules').querySelectorAll('input'), function (c) { c.checked = false; });
    countMods();
  });

  $('role-save').addEventListener('click', async function () {
    var name = ($('role-name').value || '').trim();
    if (name.length < 2) {
      $('role-msg').textContent = 'Give the role a name.';
      $('role-msg').className = 'msg err';
      return;
    }
    $('role-save').disabled = true;
    try {
      await call('/hrms/roles', { method: 'POST', body: { id: EDITING || 0, name: name, modules: pickedMods() } });
      await loadRoles();
      showRolePage('list');
    } catch (e) {
      $('role-msg').textContent = e.message;
      $('role-msg').className = 'msg err';
    } finally { $('role-save').disabled = false; }
  });

  $('role-del').addEventListener('click', async function () {
    if (!EDITING) return;
    if (!confirm('Delete this role?')) return;
    $('role-del').disabled = true;
    try {
      await call('/hrms/roles/' + EDITING, { method: 'DELETE' });
      await loadRoles();
      showRolePage('list');
    } catch (e) {
      $('role-msg').textContent = e.message;
      $('role-msg').className = 'msg err';
    } finally { $('role-del').disabled = false; }
  });

  function describeRole(id) {
    var r = ROLES.filter(function (x) { return x.id === id; })[0];
    if (!r) {
      $('pf-rolemods').innerHTML =
        '<span style="color:#b3261e">No role assigned \u2014 this person cannot open anything on the desktop.</span>';
      return;
    }
    if (r.isSuperadmin) {
      $('pf-rolemods').innerHTML = 'Every module, no restrictions.';
      return;
    }
    if (!r.modules.length) {
      $('pf-rolemods').innerHTML =
        '<span style="color:#b3261e">This role has no modules ticked yet.</span>';
      return;
    }
    var names = r.modules.map(function (k) {
      var m = MODULES.filter(function (x) { return x.key === k; })[0];
      return esc(m ? m.label : k);
    });
    $('pf-rolemods').innerHTML =
      '<b>' + names.length + ' of ' + MODULES.length + ':</b> ' + names.join(' \u00b7 ');
  }

  async function fillRolePicker(u) {
    await ensureModules();
    if (!ROLES.length) { try { ROLES = await call('/hrms/roles'); } catch (e) { ROLES = []; } }
    var cur = u.roleId || 0;
    $('pf-role').innerHTML = '<option value="0">No role (no desktop access)</option>' +
      ROLES.map(function (r) {
        return '<option value="' + r.id + '"' + (r.id === cur ? ' selected' : '') + '>' +
               esc(r.name) + (r.isSuperadmin ? ' — all access' : '') + '</option>';
      }).join('');
    describeRole(cur);
  }

  $('pf-role').addEventListener('change', async function () {
    if (!PFID) return;
    var v = parseInt($('pf-role').value, 10) || 0;
    $('pf-rolemsg').textContent = '';
    try {
      await call('/hrms/profiles/' + PFID + '/role', { method: 'POST', body: { roleId: v } });
      $('pf-rolemsg').textContent = 'Role updated.';
      $('pf-rolemsg').className = 'msg ok';
      describeRole(v);
      await loadProfiles();
    } catch (e) {
      $('pf-rolemsg').textContent = e.message;
      $('pf-rolemsg').className = 'msg err';
    }
  });

  $('pf-q').addEventListener('input', renderProfiles);
  $('pf-save').addEventListener('click', function () { savePassword(false); });
  $('pf-clear').addEventListener('click', function () { savePassword(true); });
  $('pf-close').addEventListener('click', function () { openPfDrawer(false); });
  $('scrim2').addEventListener('click', function () { openPfDrawer(false); });

  $('chip').addEventListener('click', function () { openDrawer(true); });
  $('dr-close').addEventListener('click', function () { openDrawer(false); });
  $('scrim').addEventListener('click', function () { openDrawer(false); });
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') { openDrawer(false); openPfDrawer(false); }
  });

  var IC = {
    search:'<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.6-3.6"/></svg>',
    lock:  '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><rect x="4" y="10.5" width="16" height="10.5" rx="2"/><path d="M8 10.5V7a4 4 0 0 1 8 0v3.5"/></svg>',
    link:  '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M10 13.5a4 4 0 0 0 6 .5l2.5-2.5a4 4 0 0 0-5.7-5.7L11.5 7"/><path d="M14 10.5a4 4 0 0 0-6-.5L5.5 12.5a4 4 0 0 0 5.7 5.7L12.5 17"/></svg>',
    warn:  '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M12 8.5v5"/><path d="M12 17h.01"/><path d="M10.3 3.9 1.9 18.4A2 2 0 0 0 3.6 21.4h16.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z"/></svg>',
    plug:  '<svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"><path d="M9 2.5v6M15 2.5v6"/><path d="M6 8.5h12v3a6 6 0 0 1-12 0Z"/><path d="M12 17.5v4"/></svg>'
  };

  function showState(which) {
    show($('st-load'),   which === 'load');
    show($('st-signin'), which === 'signin');
    show($('st-error'),  which === 'error');
  }

  function fail(icon, title, body) {
    $('er-ic').innerHTML  = IC[icon] || IC.warn;
    $('er-title').textContent = title;
    $('er-body').textContent = body;
    showState('error');
    document.title = title + ' — HRMS — CRMRS';
  }

  (async function boot() {
    showState('load');

    if (!slug) {
      fail('link', 'Page not found',
        'This address is incomplete. Open HRMS from the CRMRS desktop app.');
      return;
    }

    if (token()) {
      try { return enterApp(await call('/hrms/me')); }
      catch (e) { token(''); }
    }

    var r;
    try {
      r = await call('/hrms/status?slug=' + encodeURIComponent(slug));
    } catch (e) {
      var code = e.code || '';
      if (code === 'not_found') {
        fail('search', 'Page not found',
          'No agency matches this address.');
      } else if (code === 'not_enabled') {
        fail('plug', 'HRMS isn’t switched on',
          'HRMS is not enabled for this agency. Contact CRMRS to have it turned on.');
      } else if (code === 'not_active') {
        fail('lock', 'This agency isn’t active',
          'This account is not active, so HRMS cannot be opened. Contact CRMRS support.');
      } else {
        fail('warn', 'We can’t reach HRMS right now',
          'Check your connection and try again in a moment.');
      }
      return;
    }

    $('id-name').textContent = r.agencyName || '';
    $('id-logo').src = logoUrl(r.logoPath);
    $('mask').textContent = r.email || '';
    $('mask2').textContent = r.email || '';
    document.title = (r.agencyName ? r.agencyName + ' — ' : '') + 'HRMS — CRMRS';
    showState('signin');
  })();

  var cooldownTimer = null;

  function startCooldown(seconds) {
    var left = seconds;
    var send = $('send'), again = $('resend');
    if (cooldownTimer) clearInterval(cooldownTimer);
    function paint() {
      if (left <= 0) {
        clearInterval(cooldownTimer); cooldownTimer = null;
        send.disabled = false; again.disabled = false;
        send.textContent = 'Send code';
        again.textContent = 'Send a new code';
        return;
      }
      send.disabled = true; again.disabled = true;
      send.textContent = 'Send code in ' + left + 's';
      again.textContent = 'Send a new code in ' + left + 's';
      left--;
    }
    paint();
    cooldownTimer = setInterval(paint, 1000);
  }

  async function sendCode(btn) {
    if (cooldownTimer) return;
    busy(btn, true, ' Sending');
    say('');
    try {
      var r = await call('/hrms/otp/request', { method: 'POST', body: { slug: slug } });
      $('mask').textContent = r.email || '';
      $('mask2').textContent = r.email || '';
      show($('step-send'), false);
      show($('step-code'), true);
      say('Code sent.', 'ok');
      startCooldown(60);
      $('code').focus();
    } catch (e) {
      say(e.message, 'err');
      if (e.status === 429) startCooldown(e.retryAfter || 60);
    } finally { busy(btn, false); }
  }

  $('send').addEventListener('click', function () { sendCode($('send')); });
  $('resend').addEventListener('click', function () { sendCode($('verify')); });

  $('code').addEventListener('input', function () {
    this.value = this.value.replace(/\D/g, '').slice(0, 6);
    if (this.value.length === 6) $('verify').click();
  });

  $('verify').addEventListener('click', async function () {
    var code = $('code').value.trim();
    if (code.length !== 6) { say('Enter all 6 digits.', 'err'); return; }
    busy($('verify'), true, ' Verifying');
    say('');
    try {
      var r = await call('/hrms/otp/verify', { method: 'POST', body: { slug: slug, code: code } });
      token(r.token);
      enterApp(await call('/hrms/me'));
    } catch (e) {
      say(e.message, 'err');
      $('code').value = '';
      $('code').focus();
    } finally { busy($('verify'), false); }
  });

  $('dr-signout').addEventListener('click', function () { $('logout').click(); });

  $('logout').addEventListener('click', async function () {
    try { await call('/hrms/logout', { method: 'POST' }); } catch (e) {}
    token('');
    location.reload();
  });

})();
