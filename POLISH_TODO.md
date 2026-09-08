# VAMP4 Polish Backlog

Shared checkpoint file. This — not conversation context — is the source of truth across sessions.
Rule: every completed item ends in its own git commit referencing the item below. No item is "done" without a commit hash here.

Status: `[ ]` not started · `[~]` in progress · `[x]` done (commit hash)

---

## 0. Baseline safety (blocking — needs your call, not an agent's)
- [ ] ~150 files uncommitted on `shader-revamp` (last real commit `3bf1bca`): modified core scripts/scenes/prefabs/materials + entirely new untracked systems (SlotMachine3Reel, WeatherWheelUI, DropPuzzleJuice/FlowManager, Assets/Import, Assets/RiceballKit) and some junk (`PuzzlePrefabLoader.cs.bak`, `.lnk` shortcuts). Decide: one WIP snapshot commit, or split into logical commits, or prune junk first. — **Owner: you.**

## 1. Shader / material finish-up
- [ ] Review what's actually changed in `rice.mat` + the dungeon-pack materials vs. what `shader-revamp` intended; finish or revert stragglers. Skill: `unity-shaders`. Model: Opus (design/visual judgment).

## 2. ECS perf audit (read-only first)
- [ ] Grep `Assets/Scripts` for `SystemBase`/`.WithoutBurst()`/structural `AddComponent`/`RemoveComponent` outside the already-optimized rice systems (baseline: `ECS_OPTIMIZATIONS.md`). No edits — just a findings file. Model: **Sonnet**. — *dispatched as the live demo, see below.*
- [ ] Once findings exist: fix confirmed regressions one at a time, each its own commit. Model: Sonnet unless a fix is architecturally ambiguous.

## 3. Known-behavior review (design call, not a bug fix)
- [ ] `HelperAI.DepositCollectedRice()` (`HelperAI.cs:401`) calls `AddCurrency()` not `AddRice()`. `CLAUDE.md`'s own issue table flags this as "known behavior — fix if helpers should add to rice count instead." That's a design decision, not a confirmed bug — needs your call before any agent touches it. **Owner: you.**

## 4. Console/error cleanup
- [ ] Play-mode pass on `FPS_Collect` + `DropPuzzle` via `Unity_GetConsoleLogs`/`Unity_ReadConsole`, turned into a checklist. Needs unity-mcp — live-Editor lane, run one at a time. Model: Sonnet.

## 5. Lighting / visual bugs
- [ ] Close out the `CLUSTER_LIGHT_LOOP`/`FORWARD_PLUS`/`RealtimeLights.hlsl` investigation from earlier work. Needs unity-mcp scene capture for before/after. Model: Opus (nontrivial render-pipeline reasoning).

---

## Standing rules for every item
- Default every subagent to **Sonnet**. Escalate to Opus only for genuinely ambiguous/creative reasoning (visual/design judgment, unclear architecture tradeoffs) — not for mechanical fixes or audits.
- Static-analysis items (Read/Grep only, no MCP) can run **in parallel**. Anything touching the live Editor via unity-mcp runs **one at a time**.
- Every subagent gets a closed stop condition, not an open mandate. Point it at exact files/lines when known.
- Subagents report a short summary + commit hash back — never a pasted diff. Keeps the orchestrator's context (and usage) cheap.
- If a session runs out of usage mid-item, nothing is lost beyond the current uncommitted step — resume from this file.
