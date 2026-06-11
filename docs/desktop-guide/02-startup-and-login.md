# 02 — Startup & Login 🚪

What happens from the moment you double-click `CRMRS.exe` until you see the dashboard.

## The code involved
- [App.xaml.cs](../../VKdesktopapp/App.xaml.cs) — runs first, sets everything up.
- [LoginWindow.xaml.cs](../../VKdesktopapp/LoginWindow.xaml.cs) — the sign-in screen.
- [MainWindow.xaml.cs](../../VKdesktopapp/MainWindow.xaml.cs) — the main app window (opens after login).

## Step 1 — App starts up (`App.xaml.cs`)

Before any window appears, `App` does the housekeeping:

1. **Reads settings** — the server URL (`https://api.crmrecoverysoftware.com/`) and the app key. It even auto-fixes old/stale saved URLs so the app always points at the right server.
2. **Creates one shared `HttpClient`** — the single object used for *all* internet calls. It's set up to:
   - accept **gzip** (compressed) responses so big lists download ~5–10× smaller,
   - keep connections alive,
   - time out after 5 minutes (long enough for big uploads).
3. **Warms up the connection** — it quietly "pings" the server in the background while the login screen draws, so your first real click is fast instead of waiting for a cold connection.

Think of this as the app *putting on its shoes* before it does anything.

## Step 2 — The login screen (`LoginWindow`)

You see a window asking for **email + password**. That's *all* it needs — no API key typed, nothing else.

When you click **Sign In**, the code runs `Login()`:

1. It packages `{ email, password }` and sends a `POST` to **`/api/agency/desktop/login`**.
2. The **server** looks up your agency in the `crm_master.agencies` table by email, and checks your password against the stored **hash** (a scrambled, one-way version — your real password is never stored).
3. If the password matches **and** your agency status is `approved`, the server creates a **login token** and sends it back, along with your agency name, logo, and contact info.

> 🔐 **The token** is a signed string (starts with `agt1.`). Inside it, invisibly, is *your agency's ID and slug* plus an expiry date. The server signs it so nobody can fake one. This token is what makes you "you" for the rest of the session.

## Step 3 — The token gets attached to everything

Back in the app:

1. `App.SignedAppUser` remembers who you are.
2. `App.SetAuthToken(token)` attaches the token to the shared `HttpClient` as a `Bearer` header.
3. From now on, **every** request the app makes automatically carries that token → the server always knows which agency's database to open.

It also caches your agency's **logo + name** locally, so next time the login screen shows *your* branding instead of the generic CRMRS look.

## Step 4 — The main window opens (`MainWindow`)

The login window hides, and `MainWindow` appears — the actual app with the menu and dashboard. (Covered in [03 — The Shell & Navigation](03-shell-and-navigation.md).)

## The whole thing in one picture

```
 double-click CRMRS.exe
        │
        ▼
 App.xaml.cs  ── sets up HttpClient, warms up connection
        │
        ▼
 LoginWindow ── you type email + password ── click Sign In
        │
        │  POST /api/agency/desktop/login   { email, password }
        ▼
 SERVER  ── checks crm_master.agencies (password hash, status=approved)
        │
        │  returns  { token: "agt1...", agencyName, logo, ... }
        ▼
 App.SetAuthToken(token)   ← token now rides on every future request
        │
        ▼
 MainWindow opens  →  Dashboard
```

## Things worth knowing

- **Wrong password / not approved** → the server returns an error message, and the login screen shows it ("Invalid email or password", "still awaiting verification", etc.).
- **No internet** → the app shows a friendly "Cannot reach the server" message instead of crashing.
- **The token expires** after a set period; when it does, the server replies "session expired" and you simply sign in again (you get a fresh token).
- **Re-verify password without logging out** — some screens (like deleting data) can re-check your password quietly via the same login endpoint, without disturbing your session.

➡️ Next: [03 — The Shell & Navigation](03-shell-and-navigation.md)
