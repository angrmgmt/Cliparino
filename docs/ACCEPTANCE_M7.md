# ACCEPTANCE_M7.md — Milestone 7: Self-Heal

Purpose:
Verify automatic recovery from common failures.

---

## A. Preconditions
- M6 acceptance PASSED

---

## B. Failure Scenarios

Test:
- Twitch disconnect
- OBS restart
- Player server restart
- Bad clip URL

**Expected**
- App recovers without user action
- No permanent stuck state
- Queue continues after recovery

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Exponential backoff implemented in all reconnection scenarios
- OBS health supervisor auto-reconnects ([ObsHealthSupervisor.cs](../src/Cliparino.Core/Services/ObsHealthSupervisor.cs))
- Twitch transport redundancy (EventSub WS + IRC fallback)
- Clip quarantine (3-strike rule) in PlaybackEngine
- Queue continues after recovery from failures
