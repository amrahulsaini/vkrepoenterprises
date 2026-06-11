# 13 — Support 🎧

Raise a help ticket to the CRMRS team and chat back-and-forth on it (with an optional screenshot). Opens as its own window from the headset icon.

## Where the code is
- [SupportWindow.xaml.cs](../../VKdesktopapp/SupportWindow.xaml.cs) — list + create tickets.
- [TicketThreadWindow.xaml.cs](../../VKdesktopapp/TicketThreadWindow.xaml.cs) — the message thread of one ticket.

## Where the data lives

Tickets are stored in the shared `crm_master` database (`support_tickets` + `support_ticket_messages`), so the CRMRS admin team can see every agency's tickets in their own portal and reply. These endpoints are authenticated by your **login token** (`/api/agency/desktop/...`), not the API key.

## How it loads

| Action | → endpoint |
|---|---|
| Open Support | `GetMyTicketsAsync()` → `GET /api/agency/desktop/tickets` |
| Raise a ticket | `CreateTicketAsync(subject, message, screenshot)` → `POST /api/agency/desktop/tickets` |
| Reply on a ticket | `PostTicketMessageAsync(id, body)` → `POST /api/agency/desktop/tickets/{id}/messages` |

## What you see
- A list of your tickets with their status (open / in progress / resolved).
- Open one → the full conversation; you can post a reply.
- The screenshot you attach is uploaded and shown in the thread.

## The unread badge (a nice touch)

The main window checks every 60 seconds whether CRMRS has posted new admin replies, and shows a **red count** on the 🎧 icon. It works by counting admin messages and comparing to the count the last time you opened Support (saved in a tiny local file). Open Support → the badge clears.

## Trace it end-to-end (report a problem)

1. You click Support → New ticket, type a subject + description, optionally paste a screenshot, click Send.
2. Window → `CreateTicketAsync(...)` → `POST /api/agency/desktop/tickets`.
3. Server saves a row in `crm_master.support_tickets` (and the screenshot to disk), tagged with your agency.
4. The CRMRS team sees it in their manage portal and replies → their reply becomes a message on the ticket.
5. Within a minute, your desktop's 🎧 badge lights up; you open the thread and read the reply.

## How errors get reported automatically

Separately, whenever the desktop app hits an unexpected error, it quietly sends the details to the server (`/api/agency/desktop/client-error`) so the CRMRS team can see failures centrally — you don't have to report every glitch manually.

➡️ Next: [14 — Direct Data](14-direct-data.md)
