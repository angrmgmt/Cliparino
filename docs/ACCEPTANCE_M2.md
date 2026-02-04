# ACCEPTANCE_M2.md — Milestone 2: Player, Queue, OBS Ensure

Purpose:
Verify that Cliparino can play clips end-to-end locally and enforce OBS desired state,
without Twitch chat integration.

---

## A. Preconditions
- M1 acceptance PASSED
- OBS installed (clean profile acceptable)
- Streamer.bot NOT installed

---

## B. Player Server

### B1. Playback API
- Player server exposes endpoints for:
    - play clip
    - stop
    - replay

**Expected**
- Invoking play causes clip playback in browser source
- Stop halts playback immediately
- Replay replays last clip

---

### B2. Player states
- Idle
- Loading
- Playing
- Error

**Expected**
- State transitions occur correctly
- Error state does not crash server

---

## C. Queue Behavior

**Expected**
- Multiple clips enqueue and play FIFO
- Queue survives stop/replay operations
- Failed clip is skipped without stalling queue

---

## D. OBS Desired State

### D1. Creation
- Scene auto-created if missing
- Browser source auto-created if missing
- Correct URL + dimensions applied

### D2. Repair
- Restart OBS → Cliparino reconnects
- Delete browser source → Cliparino recreates

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Player server implemented ([Program.cs:47-97](../src/Cliparino.Core/Program.cs))
- Queue engine ([ClipQueue.cs](../src/Cliparino.Core/Services/ClipQueue.cs))
- Playback state machine ([PlaybackEngine.cs](../src/Cliparino.Core/Services/PlaybackEngine.cs))
- OBS automation ([ObsController.cs](../src/Cliparino.Core/Services/ObsController.cs))
- OBS health supervisor ([ObsHealthSupervisor.cs](../src/Cliparino.Core/Services/ObsHealthSupervisor.cs))