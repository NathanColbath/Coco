# Comic Studio

Turn ideas into stories, and stories into comics. NovelAI does the writing and
the art; this repo holds the canon, the context and the pipeline that keeps
both consistent.

- **Frontend** — Angular 22 (standalone components, signals, Material 3) at the repo root.
- **Backend** — ASP.NET Core 10 + EF Core 10 on SQLite in [`api/`](api).
- **Story engine** — NovelAI text models (GLM-4.6 / Xialong chat endpoint, legacy completion for Kayra/Erato).
- **Art engine** — NovelAI Diffusion (image API), with character reference sheets, precise reference and vibe transfer for consistency.
- **Output** — server-side page compositing (SkiaSharp) exporting PNG, PDF and CBZ.

## Requirements

| Tool | Version |
| --- | --- |
| Node.js | 24.21+ (pinned in `.node-version`; run `fnm use`) |
| .NET SDK | 10.0+ |
| EF Core tools | `dotnet tool install --global dotnet-ef` |

### Known environment quirks

Angular 22's build uses `sass-embedded`, whose bundled Dart VM requires
**macOS 14+**. On older macOS (this project was set up on 12.7) Sass compilation
fails with `VM initialization failed`. The npm scripts therefore set
`NG_BUILD_SASS_EMBEDDED=false` (via `cross-env`) to use the pure-JS Dart Sass
compiler, and `sass` is a devDependency so that fallback is always available.
On macOS 14+ you can drop that env var to get the faster embedded compiler.

## Getting started

```bash
# 1. Backend (http://localhost:5240)
npm run api:dev

# 2. Frontend (http://localhost:4200, proxies /api -> :5240)
npm start
```

The database is created and seeded on first run at `api/App_Data/comicstudio.db`.

## Scripts

| Script | What it does |
| --- | --- |
| `npm start` | Angular dev server with the API proxy |
| `npm run api:dev` | Backend with hot reload |
| `npm run dev:stop` | Stops the backend watcher, the dev server, and any orphaned children |
| `npm run build` | Production frontend bundle into `dist/` |
| `npm run build:api` | Frontend bundle into `api/wwwroot` for a single-artifact deploy |
| `npm test` | Unit tests (Vitest) |
| `npm run test:api` | Backend tests (xUnit — context assembly, lorebook matching, token counting) |
| `npm run hub:smoke -- <jwt> <storyId>` | Exercises the realtime generation channel end to end |
| `npm run api:types` | Regenerate `src/app/core/api/schema.d.ts` from the running API (pinned `openapi-typescript` via npx) |
| `npm run db:update` | Apply EF Core migrations |

## Layout

```
src/app/core/          cross-cutting: typed API client, theme, (auth, realtime)
src/app/shared/ui/     the shared Material kit — the only source of UI components
src/app/features/      lazy-loaded feature areas
api/Domain/            entities
api/Data/              DbContext, configurations, migrations, seed
api/Features/          vertical slices (one folder per API area)
```

## Conventions

- **The model registry is data, not code.** NovelAI model ids, context windows,
  tier requirements and capabilities live in `api/Data/Seed/AiModelSeeder.cs` and
  are served to the SPA. NovelAI moves fast (for example the Kayra → GLM text API
  migration); those changes should be seed edits.
- **Secrets never reach the browser.** Per-user NovelAI tokens are encrypted with
  ASP.NET Data Protection and only ever used server-side.
- **Typed contracts.** The SPA consumes the API through `openapi-fetch` types
  generated from the API's own OpenAPI document — run `npm run api:types` after
  changing a controller.
- **Reusable UI lives in `src/app/shared/ui`.** Features compose it rather than
  styling Material directly.

## Status

**Milestone 0 — foundations (done)**: both apps scaffolded and wired together,
SQLite + EF Core with the seeded NovelAI model registry, OpenAPI→TypeScript
contract pipeline, Material 3 theming with light/dark, and a shared UI kit
(`page-header`, `section-card`, `status-chip`, `empty-state`).

**Milestone 1 — accounts & NovelAI connection (done)**: ASP.NET Identity with
JWT auth, per-user NovelAI persistent tokens encrypted at rest (Data Protection,
scoped per user id), tier/Anlas detection from `/user/subscription`, tier-aware
model defaults in Settings, auth guard + shell with account menu.

**Milestone 2 — the story core (done)**: story text is a tree of blocks with a
cursor, so retries and edits become *variants* rather than overwrites. A
deterministic `ContextAssembler` builds the exact prompt — NovelAI's default GLM
system prompt, memory with an auto-composed ATTG line, lorebook entries placed by
insertion order/position/key-relative matching, the story trimmed to fit the
tier-aware window, the author's note spliced in N lines from the bottom, and a
per-generation instruction. Every generation stores its trace, and the editor's
context viewer renders it: budget bar, per-segment token counts, trims and
warnings.

Text generation streams over SignalR (`/hubs/generation`) with
`openai-chat` (GLM-4.6 / Xialong via `text.novelai.net/oa/v1`) and
`legacy-completion` (Kayra / Erato / Clio) transports behind one interface, plus
a diagnostics endpoint that probes each transport with your own token.

Next up — **the manga pipeline**: story → curated panels → prompts → art →
readable pages. See the comics section below.

**Slice A/B/C — manga pipeline (in progress)**:

- **Comic projects** — create from any story, right-to-left by default, black and
  white manga style preset, page canvas, dialogue adaptation dial.
- **Curator** — NovelAI's text model reads the story and produces structured
  intent, never prompts: cast discovery, scene segmentation, panel breakdown with
  shots and dialogue, then deterministic page packing against a 14-template manga
  layout library. Strict JSON with a tolerant parser and one automatic repair pass.
- **Deterministic composer** — turns panel intent plus the project's style preset
  and character canon into the actual NovelAI request: base caption, per-character
  captions with frame positions, negatives, resolution chosen to sit inside the
  Opus free-generation envelope, and Precise Reference arrays when a character has
  a locked sheet.
- **Image pipeline** — serialised job queue (NovelAI deprecated concurrent
  generation), ZIP decoding, content-addressed storage, per-job Anlas cost measured
  as a subscription balance delta, progress over SignalR.
- **Reader** — pages composed in the browser from panel rects, right-to-left or
  left-to-right, with bubbles, narration boxes and SFX lettering.
