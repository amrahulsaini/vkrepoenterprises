# 14 — Direct Data 🔗

A more advanced/optional feature: receive data files **pushed directly by a provider** (e.g. a bank like HDB) into the system, instead of you uploading Excel by hand.

## Where the code is
- [DirectData/DirectDataPage.xaml.cs](../../VKdesktopapp/DirectData/DirectDataPage.xaml.cs)
- [DirectData/AddCredentialDialog.xaml.cs](../../VKdesktopapp/DirectData/AddCredentialDialog.xaml.cs)

## The idea (webhooks)

A "webhook" is when an outside system **sends you data automatically** by calling your server, rather than you fetching it. Here, a provider can POST files to `POST /api/webhooks/provider/HDB`, and they land in webhook tables. This page lets you see those files and manage the login accounts that are allowed to push them.

## How it loads

| Action | → endpoint | → table |
|---|---|---|
| List received files | `GET /api/webhooks/files` | `webhook_files` |
| Download a file | `GET /api/webhooks/files/{id}/download` | `webhook_files` (+ disk) |
| List push-accounts | `GET /api/webhooks/users` | `webhook_users` |
| Add a push-account | `POST /api/webhooks/users` | `webhook_users` |
| Remove a push-account | `DELETE /api/webhooks/users/{id}` | `webhook_users` |

## What you do here
- See the list of files providers have pushed (name, date, count).
- Download a pushed file.
- Create / delete the **credentials** a provider uses to push (the `AddCredentialDialog`).

## The three webhook tables

| Table | Holds |
|---|---|
| `webhook_banks` | which providers/banks are set up |
| `webhook_files` | each pushed file's info |
| `webhook_users` | username + password (hashed) accounts allowed to push |

## Trace it end-to-end (a provider pushes a file)

1. You create a push-account here (`webhook_users`) and give those credentials to the provider.
2. The provider's system calls `POST /api/webhooks/provider/HDB` with those credentials + a file.
3. The server saves the file and records it in `webhook_files`.
4. Back on this page, **List files** → `GET /api/webhooks/files` → you see the new file and can download it.

## When you'd use this vs Upload Records

- **Upload Records ([08](08-upload-records.md))** — you have an Excel file and load it yourself. This is the normal path.
- **Direct Data** — a provider is integrated to push files automatically. Optional, only if you've set up such an integration.

---

🎉 That's the whole desktop app. If you read [01](01-big-picture.md) → here, you now understand: how it starts, how it logs in, how every page asks the server for data through `DesktopApiClient`, which tables each page touches, and which buttons are dangerous. Every screen is the same loop: **page → DesktopApiClient → server → database → back.**
