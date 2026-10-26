# Bridge to the Z code agent on the build laptop

**Status: design, not built.** Nothing here runs yet. The goal: a cheap cloud session (a small model at low effort) hands a job to the
Z code agent that runs on the personal build laptop, and reads the result back, so the Z code plan does the building and the cloud
session only coordinates.

## What was checked (2026-10-26)

| Question | Finding | How known |
|---|---|---|
| Does Z code have a Telegram link? | Yes. Third-party coverage lists Telegram next to WeChat and Feishu as remote triggers. One tutorial describes: Settings, Remote Control, choose Telegram, pair with a `/connect` code shown on the desktop, manual bot-token fallback. | Third-party pages only. No official page found. **Unverified.** |
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

### Lane A: job queue in git (recommended; no new secret, no new host)

The cloud session pushes a job file `jobs/<id>.json` to a branch named `zbridge` of the repository the job is about. A guarded poller on the
laptop fetches that branch on an interval, checks the job against the rules above, gives it to Z code, and pushes `results/<id>.json` (status, branch, PR link, short summary) back to the same branch. The cloud session reads the result with the normal GitHub tools.

- Job schema (all fields required): `id`, `repo` (one of the allowed three), `base` (branch), `task` (text, size-limited), `allowed_paths`, `max_subagents` (at most 4), `deliverable` (`draft-pr`), `expires`.
- The poller accepts a job only if the commit author is the owner account, the repo and paths pass, and the job has not expired. It never executes `task` itself; it only hands it to Z code.
- Open point: how the poller hands a job to Z code. Needs one check on the laptop: does Z code have a command line, a headless mode or a watched folder? If not, the hand-off falls to lane B or C.

### Lane B: Telegram (what the owner proposed)

Needs a private chat with an allowlist of exactly the owner's chat id, the bot token stored only on the laptop, and the cloud environment to allow `api.telegram.org`. Because bots may not see each other, the cloud side would be a second bot in a private group with the owner and the Z code bot. First step is therefore an experiment, not a build: pair Z code with a bot and send it a status request from the owner's phone; then, with a disposable test bot, learn whether a bot-sent message reaches it.

### Lane C: drive the Z code window with computer use

A Claude session linked to the laptop types the job into the Z code window and reads the answer from the screen. Works today with only the owner's app-access approval; slow and screenshot-heavy, so not for a small model. Good for the first contact and as the stopgap for the hand-off in lane A.

### Lane D: the Z code remote web page (not recommended as an integration)

The remote page is an undocumented web client; its link carries a session id that is a bearer credential for the whole agent. Remote connection is off by default and the link changes every time it is switched off and on. Reverse-engineering it would be fragile. Use it by hand in a browser, not from automation.

## Dependency nodes this adds

See `docs/dependencies.md`: Telegram Bot API, Z code desktop app, Z.ai hosted model service, and a placeholder for a browser/app driving interface (`cm.ii` candidate, owner note 2026-10-26: "Driving other websites remotely is something CM will have to do as an MCP").
No NuGet package is added: the Telegram Bot API is plain HTTPS and `HttpClient` is enough.
