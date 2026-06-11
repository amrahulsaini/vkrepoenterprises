# Publishing CRMRS to the Microsoft Store (MSIX)

This folder is the **MSIX packaging project** for the universal CRMRS desktop app.
It wraps the WPF project (`..\VKdesktopapp\CRMRSDesktopApp.csproj`) into a Store-ready
`.msixupload`. The Microsoft Store **signs the package for you**, so you do **not**
need to buy a code-signing certificate.

Ship the **generic / universal** build (the one with **no** `Resources\branding.json`):
it shows the CRMRS logo, and after any agency signs in it caches and shows *their*
logo + name on the next launch. One app, every agency.

---

## 0. One-time prerequisites

1. **Visual Studio 2022** with these workloads (Visual Studio Installer ▸ Modify):
   - **.NET desktop development**
   - **Universal Windows Platform development** → and tick
     **"Windows application packaging"** / the Windows 11 SDK (10.0.22621).
   Without this, the `.wapproj` shows as *"project type not supported"*.
2. **Partner Center developer account** — https://partner.microsoft.com/dashboard
   → register as an app developer. One-time fee: **$19** (individual) / **$99** (company).
3. Make sure `..\VKdesktopapp\Resources\branding.json` is **absent** so this is the
   universal CRMRS build (per-agency builds bake that file in — not for the Store).

---

## 1. Add this project to the solution (one time)

In Visual Studio, open `CRMRSDesktopApp.sln`, then:

- **Solution Explorer ▸ right-click the solution ▸ Add ▸ Existing Project…**
- pick `CRMRS.Package\CRMRS.Package.wapproj`.
- If prompted about solution platforms, accept (it adds **x64**).
- Right-click the package project ▸ **Set as Startup Project** (so F5 runs the packaged
  app and you can test it under MSIX before uploading).

The WPF project is already referenced as the entry point, the manifest and all tile
images are already filled in — nothing else to wire up.

> Tip: set the toolbar to **Release | x64** for Store builds.

---

## 2. Reserve the app name in Partner Center (one time)

Partner Center ▸ **Apps and games ▸ New product ▸ MSIX or PWA app** ▸
**Reserve a product name** → e.g. `CRMRS`. (If taken, try `CRMRS Recovery`.)

---

## 3. Associate the package with the Store

In Visual Studio: right-click **CRMRS.Package ▸ Publish ▸ Associate App with the Store…**
→ sign in with the Partner Center account → pick the reserved name.

This **rewrites** `<Identity Name / Publisher / PublisherDisplayName>` in
`Package.appxmanifest` with your real Store values. (The values in there now are
placeholders — this step is what makes them correct.)

---

## 4. Create the upload package

Right-click **CRMRS.Package ▸ Publish ▸ Create App Packages…**

- **Distribution method:** *Microsoft Store under <your account>*
- Pick the reserved name.
- **Architecture:** tick **x64** (the app is x64). Untick x86/ARM64.
- **Solution config:** Release | x64.
- Finish. It produces an **`.msixupload`** under
  `CRMRS.Package\AppPackages\…` and runs the **Windows App Certification Kit (WACK)**.
  Fix any WACK failures it reports before uploading.

---

## 5. Submit in Partner Center

Open your reserved product ▸ **Start your submission**:

1. **Packages** → upload the `.msixupload` from step 4.
2. **Pricing and availability** → Free, choose markets (India + wherever your agencies are).
3. **Properties** → category *Business*; declare it handles business data.
4. **Age ratings** → fill the questionnaire (no objectionable content → typically 3+).
5. **Store listing** → description, **at least one screenshot** (1366×768 or larger),
   the CRMRS logo, and a **Privacy policy URL**
   (`https://crmrecoverysoftware.com/privacy` — required because the app collects logins/data).
6. **Submit** → certification usually completes in a few hours to ~3 days.

---

## 6. Shipping updates later

Bump `<Identity Version>` in `Package.appxmanifest` (keep the 4th number `0`, e.g.
`1.0.1.0`), redo **Create App Packages**, and upload the new `.msixupload` as a new
submission. The Store pushes the update to all installed users automatically.

---

## Notes / gotchas (already handled, good to know)

- **Full trust:** `runFullTrust` is in the manifest — auto-approved for desktop apps.
- **Writable paths:** the app caches the agency logo to `%LocalAppData%\CRMS` (works
  under MSIX). Anything the app writes next to its EXE (e.g. the `logs` folder) is
  transparently redirected by MSIX to the package's writable store — no crash, but if
  you want logs in an obvious place, point them at `%LocalAppData%` later.
- **WebView2:** works in a packaged app; the Evergreen runtime ships on Win10/11.
- **No cert:** keep `AppxPackageSigningEnabled=False` — the Store does the signing.
- **Identity:** never hand-edit `<Identity>` after step 3; let "Associate" own it.
