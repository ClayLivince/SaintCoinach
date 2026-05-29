# Definition Sharing Server — Session Compass

> Carry this doc into the dedicated server-building sessions. Sections marked
> **[YOUR INPUT]** are deliberate open slots — fill them in as you decide.
> The Godbert client already codes against the API contract below (see
> `Godbert/Repositories/DefinitionSyncClient.cs`), so the server only has to
> honor these endpoints to "just work."

## Purpose

A small service that lets a trusted circle of FFXIV source-checkers **share and
update definition files** (the `SaintCoinach/Definitions/*.json` column metadata)
between Godbert builds — and, in a later phase, share **icon-set metadata** the
UI tab scans. Today each person edits definitions locally and the only
distribution is a one-time copy at build time; this server makes updates flow
continuously.

**Key principle: the client speaks plain HTTP. Git (if used) is a server-side
storage detail only — no player needs git installed.**

## Core model

- A "definition set" = ~810 JSON files + a version string `yyyymmdd-vNNNN`
  (decoupled from the FFXIV game version; increment `N` within a day).
- The server holds the source of truth. `manifest.json` lists the current
  version and a **SHA-256 per file** so clients download only what changed.
- On launch the client fetches the manifest, diffs hashes against its local
  `Definitions/`, and downloads changed files.
- "Submit to server" pushes one edited sheet; the server validates it, writes
  the file, bumps the version, and (if git-backed) commits.

> ⚠️ Client timing note (current stub behavior): the Godbert realm reads
> `Definitions/` at construction, so files downloaded on launch apply on the
> **next** launch. The client logs "downloaded N files, restart to apply"
> rather than hot-swapping mid-session. 
> **[YOUR INPUT: do you want a pre-realm
> update gate that downloads BEFORE the realm loads, so updates apply same
> session? That means running the check synchronously at startup with a short
> timeout.]**
**answer**: I think it is okay to initialize the app by the old definition, and then async-ly update the definition.
I have noticed that if we change the definition (save in the definition editor panel) and the view in data tab,
It could read the changed definition. So it's not a big enought problem I presume? Can you check on it?

## Identity / write-gate

- Username + 4-digit PIN, **provisioned server-side** (you add users manually
  and hand out credentials). Reads are public; only `/submit` requires valid
  credentials.
- The PIN is a **write-gate token, not real security**. Use HTTPS if the server
  is ever public.
- **[YOUR INPUT: rate limiting? per-user submit log/audit trail? credential
  revocation? max submissions per hour?]**
  **Answer**: yes, audit trail is a necessary. currently, let's set a 100 updates per day for each user? and I am not know which value is suitable for other things. Can you recommend a save value for the project?

## Server shape — pick during the server sessions

- **(A) One small Flask/FastAPI service, git-backed internally** *(default
  recommendation)*: serves pull + accepts submit over HTTP; runs
  `git add/commit` on each accepted submission. Simplest single thing to run.
- **(B) Static host for reads + tiny submit service for writes**: definition
  files + `manifest.json` served as static files (e.g. nginx, GitHub raw, or
  Pages) so reads survive the submit service being down; a small endpoint
  handles only the gated writes and regenerates the manifest.
- Either way the **client stays git-free**.
- **[YOUR INPUT: chosen shape + hosting location / domain / TLS]**
**Answers**: I am okay with option A, I have several domains, please preserve me a ENV or option for me to input the domain options.
I hope the server has a manager backend and I am able to see who at when submitted what change, (also the place for me to add users)

## API contract (the client already implements this)

```
GET  {base}/manifest.json
     → 200 { "version": "yyyymmdd-vNNNN",
             "files": { "Item": "<sha256-hex>", "TerritoryType": "<sha256-hex>", ... } }

GET  {base}/definitions/<Sheet>.json
     → 200 raw definition file (UTF-8 JSON, same shape as on disk)

POST {base}/submit
     body  { "user": "...", "pin": "1234", "sheet": "TerritoryType", "json": "<full file text>" }
     → 200 { "ok": true,  "newVersion": "yyyymmdd-vNNNN" }
     → 200 { "ok": false, "error": "bad pin" | "invalid json" | ... }

(phase 2 — icon sets)
GET  {base}/iconsets/manifest.json
GET  {base}/iconsets/<id>.json
POST {base}/iconsets/submit
```

SHA-256 is computed over the **raw file bytes**; the client hashes its local
files the same way (`DefinitionSyncClient.LocalHash`). Keep server-side
serialization byte-stable (`JsonConvert.SerializeObject(obj, Formatting.Indented)`,
matching `ARealmReversed.SheetToJson`) so unchanged files don't show as changed.

## Validation on submit (server-side)

- Parse the submitted JSON; reject if it isn't a valid definition document.
- **[YOUR INPUT: schema checks — must have `sheet` matching the path? reject
  unknown converter `type`s? cap file size?]**
- Recommended: round-trip the JSON through a definition parser before accepting,
  so a broken submit can't poison everyone's next pull.
**Answers**: So I am thinking about a "reviewing" thing. Can we added a "review" stage? after we got submissions, I must manually "approve" on the backend? is that feasible? but it's not so "off the stage" working, And I must process those things periodically. I think it's hard to compromise both, but put Game Data on server to process and check, it's hard. Can we seperate this? users have different layers of permissions. tier I needs reviewing. Tier II could bypass it, or something.

## Versioning rules

- Each accepted submit → new `yyyymmdd-vNNNN`; `manifest.json` always reflects
  the latest accepted set.
- **[YOUR INPUT: conflict policy when two users edit the same sheet —
  last-write-wins? reject if the submitter's base version is stale (optimistic
  concurrency)? attempt a merge?]**

**answer**: I think this is rare, but if that happens, we can use the reviewing thing I proposed before?

## Update behavior on the client

- Default (stub): auto-download changed files on launch, log a "restart to
  apply" notice.
- **[YOUR INPUT: silent auto-apply vs. an explicit "N definitions updated —
  apply now?" prompt; show a changelog/diff; opt-in per user?]**

  **answer**: silently apply. show info in the status bar is okay.

## Icon-set sharing (phase 2)

- The UI tab scans icons per game version. Clients could submit "new scanned
  icon + the version it first appeared in" so the server builds an authoritative
  per-icon version history.
- Authorized users can "remark" (annotate the contents of) an icon set and share
  it, distributed via the same manifest/update mechanism.
- **[YOUR INPUT: icon-set data shape; how a set is keyed (set id / patch);
  who is allowed to remark; do scans auto-submit or require a button?]**


  **answer**: for version things, it should auto submit (if anything new happened to the existing "version.json", you can read the project)
  A version system for version.json or something shall be introduced? to detect changes or something. This part the discussed thing is rough. Can you read the project about how did I implemented the related scan and version note down things first? ? And we discuss it later, there are many ambiguities.

## Client touch-points already in place (Round 2)

- `Godbert/Settings.cs` → `DefinitionServerUrl`, `DefinitionServerUser`,
  `DefinitionServerPin` (all blank by default; blank URL = all sharing UI hidden).
- `Godbert/Repositories/DefinitionSyncClient.cs` → `CheckForUpdatesAsync`,
  `DownloadChangedAsync`, `SubmitAsync` (all inert until URL is set).
- `DefinitionViewModel.CheckForUpdatesOnLaunch()` → fire-and-forget on startup.
- "Submit to server" button in the Definition tab toolbar (hidden until URL set).
- **[YOUR INPUT: where should users enter URL/user/PIN? A Settings dialog? For
  now they're set by editing the settings.json directly.]**

  **Answer**: a setting dialog is good. We have menu bar. we can add it there. can also pop up when user clicked the commit button, or something. The URL root should have a default value, I'll give when I finished deployment part of our webserver.

## Open questions / your notes

**all my inputs**
1. We place our project under "Godbert" project, make a new and clean git repo for the server.
2. I hope git is used on server side storage, to document down the changing process. (when we create users, I manually input the email and git name for each user, and we commit the changes by their identity. is that okay?)
