# Bridge to the build-laptop agent (Hermes on the Z code plan)

**Status: design, not built.** Nothing here runs yet. The goal: a cheap cloud session (a small model at low effort) hands a job to the
agent on the personal build laptop and reads the result back, so the Z code plan does the building and the cloud session only coordinates.

**Update 2026-10-26 (owner):** the agent to drive is **Hermes**, an open-source agent (MIT, Nous Research) already installed on the laptop and
configured with the Z code plan. It has a command line, a one-shot mode and a work-queue board (`hermes kanban`), which the Z code desktop app
lacks. Hermes's own skills on the laptop describe it; the facts below marked *Hermes skill* come from them.

## What was checked (2026-10-26)

| Question | Finding | How known |
|---|---|---|
| Does Z code have a Telegram link? | Yes. Third-party coverage lists Telegram next to WeChat and Feishu as remote triggers. One tutorial describes: Settings, Remote Control, choose Telegram, pair with a `/connect` code shown on the desktop, manual bot-token fallback. | Third-party pages only. No official page found. **Unverified.** |
| Does Hermes take jobs from a script? | Yes. `hermes chat -q TEXT` and `hermes -z PROMPT` run one non-interactive query; `hermes kanban create/list/show/dispatch` is a work queue with workers per card; `hermes cron`, `hermes webhook` (HMAC-signed) and a Telegram/Discord/Slack gateway exist. | *Hermes skill* (reference text on the laptop). Flags not yet run here. |
| How does the board dispatch? | Probed on the laptop (Hermes v0.22.5, one profile, model glm-5.3 on the Z.AI provider): the dispatcher runs inside the gateway (`kanban.dispatch_in_gateway` true, every 60 s), so a created card starts within about a minute without any script ticking it. Cards in different workspaces run in parallel, cards in the same workspace must be linked parent to child. A worker gets only the card body as its brief. | Probe 2026-10-26 (read-only) and *Hermes skill* |
| How is the worker count capped? | `kanban.max_in_progress` caps running workers globally and is **unset** today; the derived default does not work on Windows, so there is no limit until it is set (`hermes config set kanban.max_in_progress 4`). | Probe, read from Hermes source; not yet tested on a running board |
| What does `hermes kanban create` accept? | `--body-file` (or stdin), `--workspace scratch\|worktree\|dir:<path>`, `--max-runtime`, `--max-retries`, `--model`, `--provider`, `--idempotency-key`, `--completion-contract` (`local-only`, `OWNER/REPO` or a PR URL), `--goal`, `--json`. | Probe (`--help`) |
| Can a cloud session reach `api.telegram.org`? | No. The cloud environment's network policy answers 403 to the CONNECT. | Tested. |
| Can a cloud session reach the Z code remote page? | No, same 403 for the Z code host. | Tested. |
| Can a Telegram bot message another bot? | As far as known the Bot API does not deliver one bot's messages to another bot, so "relay bot to Z code bot" may not work. | From memory of the Bot API. **Unverified**; one disposable test bot settles it. |

## Rules that hold for every lane

1. Anything that puts text into Z code is a command channel into an agent that runs code on the laptop. It is gated: Steven's own words enable it.
2. The Z code side sees only the repositories DMP, Gravity and IM (and scrubbed ones). A job names its repository and the bridge refuses any other.
3. Walls (from the owner): no job may touch the CMOS data folder, the claude folder, the runners or any credential.
4. A bot token, a pairing code and a remote-link session id are secrets: never in a repository, a PR, a log or a message. They live on the laptop only.
5. Fork pull requests never run on the laptop (open-source goalpost). A job comes only from a branch of an owner repository.
6. Every laptop step is a guarded owner script (DMP-011) with a read-only check, a dry run and `-Apply`.

## Lanes, in the order of recommendation

### Lane A: git job queue feeding the Hermes board (recommended; no new secret, no new host)

The cloud session pushes a job file `jobs/<id>.json` to a branch named `zbridge` of the repository the job is about. A guarded poller on the
laptop (an owner script run by a one-shot scheduled task) fetches that branch, checks the job
against the rules above, creates a Hermes card with `hermes kanban create --idempotency-key <id> --workspace dir:<clone> --body-file <job text> --max-runtime <limit> --json`
(the gateway dispatcher then starts it; the poller does not tick it), and when the card is done pushes `results/<id>.json`
(status, branch, PR link, short summary from `hermes kanban show <id> --json`) back to the same branch. The cloud session reads the result with the normal GitHub tools.

- Job schema (all fields required): `id`, `repo` (one of the allowed three), `base` (branch), `task` (size-limited text), `allowed_paths`, `max_parallel` (at most 4), `deliverable` (`draft-pr`), `expires`.
- The poller accepts a job only if the commit author is the owner account, the repo and paths pass, and the job has not expired. It never executes `task` itself; it only puts it in a card body, the card's workspace is the Z-side clone of that repository and nothing else.
- The card body carries the repository's rules and the verify command, because a Hermes worker has no other context.
- Parallelism: the cap of four workers is `kanban.max_in_progress`, which must be set to 4 **before** the first job, because the dispatcher is live and today nothing limits it. The idempotency key stops a re-polled job from creating a second card.
- Because the board records every run (`hermes kanban runs`, `tail`), usage per card is visible for free, which is the information the owner wants about the plan's rate limit.

### Lane A2: the cloud session as a Hermes user (later)

`hermes mcp serve` runs Hermes as an MCP server and `hermes send` pushes a message through a gateway platform. Neither is reachable from the cloud box (no route to the laptop), so they matter only for a future local orchestrator session on the laptop.

### Lane B: Telegram (what the owner first proposed)

Hermes has its own Telegram gateway (`hermes gateway setup`), so no Z code pairing is needed. Needs a private chat with an allowlist of exactly the owner's chat id, the bot token stored only on the laptop, and the cloud environment to allow `api.telegram.org`. Because bots may not see each other, the cloud side would be a second bot in a private group with the owner and the Z code bot. First step is therefore an experiment, not a build: pair Hermes with a bot and send it a status request from the owner's phone; then, with a disposable test bot, learn whether a bot-sent message reaches it.

### Lane C: drive an app window with computer use

A Claude session linked to the laptop types the job into the agent's window and reads the answer from the screen. Works today with only the owner's app-access approval; slow and screenshot-heavy, so not for a small model. Not needed for Hermes; kept as the generic method for apps without a command line.

### Lane D: the Z code remote web page (not recommended as an integration)

The remote page is an undocumented web client; its link carries a session id that is a bearer credential for the whole agent. This was the route for the Z code desktop app, which Hermes replaces. Remote connection is off by default and the link changes every time it is switched off and on. Reverse-engineering it would be fragile. Use it by hand in a browser, not from automation.

## Dependency nodes this adds

See `docs/dependencies.md`: Hermes agent, Telegram Bot API, Z code plan and Z.ai hosted model service, and a placeholder for a browser/app driving interface (`cm.ii` candidate, owner note 2026-10-26: "Driving other websites remotely is something CM will have to do as an MCP").
No NuGet package is added: the Telegram Bot API is plain HTTPS and `HttpClient` is enough.
