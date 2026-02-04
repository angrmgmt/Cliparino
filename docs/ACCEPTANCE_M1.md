# ACCEPTANCE_M1.md — Milestone 1: Cord Cut (Standalone Foundation)

Milestone: M1  
Canonical plan: PLAN.MD  
Canonical parity authority: PARITY_CHECKLIST.md

Purpose:
This acceptance document verifies that Cliparino has been successfully
decoupled from Streamer.bot and now runs as a standalone application.
No feature parity beyond the minimal foundation is required at this stage.

If ANY check in this document fails, Milestone M1 is NOT complete.

---

## A. Preconditions

- Streamer.bot is NOT installed on the test machine
- OBS may or may not be running (OBS is not required for M1)
- Twitch credentials are NOT required for M1
- Repository is checked out cleanly

---

## B. Structural Acceptance

### B1. Repository layout

**Expected**
- Root solution file exists (e.g. `Cliparino.sln`)
- Rewrite projects live under `/src/`
- Legacy Streamer.bot code (if retained) lives under `/legacy/`
- `/legacy/` is NOT referenced by the root solution

**Fail conditions**
- Root solution references any project under `/legacy/`
- Rewrite projects exist alongside legacy code without isolation

---

### B2. Dependency isolation

Run the following searches at repository root:

```text
Streamer.bot
CPHInlineBase
IInlineInvokeProxy
_cph.---
```

## Verdict

- [x] PASS — All M1 checks satisfied; Streamer.bot fully removed; proceed to M2
- [ ] FAIL — One or more M1 checks failed; Streamer.bot dependency remains

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Root solution file exists: [Cliparino.sln](../Cliparino.sln)
- Rewrite projects under `/src/`: Cliparino.Core and tests
- Legacy code isolated in `/legacy/` (not referenced by root solution)
- Dependency isolation verified: No Streamer.bot references in `/src/` codebase
- Standalone architecture: Windows tray application + ASP.NET Core server

Notes (optional):
- All Streamer.bot dependencies (CPHInlineBase, IInlineInvokeProxy) isolated to legacy folder
- Core application runs independently without Streamer.bot installation
