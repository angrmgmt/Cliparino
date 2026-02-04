# ACCEPTANCE_M4.md — Milestone 4: Shoutouts

Purpose:
Verify shoutout clip selection, filtering, and messaging.

---

## A. Preconditions
- M3 acceptance PASSED

---

## B. !so Command

**Expected**
- Random clip selected from target channel
- Clip enqueued and played

---

## C. Filters

Verify:
- Max clip age
- Max clip length
- Featured-first toggle

**Expected**
- Disallowed clips are never selected

---

## D. Messaging

**Expected**
- Shoutout message sent when enabled
- Empty message disables output cleanly

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Shoutout service ([ShoutoutService.cs](../src/Cliparino.Core/Services/ShoutoutService.cs))
- Featured-first toggle implemented
- Max clip length/age filters implemented
- Configurable shoutout message with placeholders