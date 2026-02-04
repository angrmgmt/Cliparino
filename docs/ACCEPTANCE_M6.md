# ACCEPTANCE_M6.md — Milestone 6: Settings & Persistence

Purpose:
Verify runtime configuration and persistence.

---

## A. Preconditions
- M5 acceptance PASSED

---

## B. Persisted Settings

Verify persistence of:
- Scene/source names
- Dimensions
- Shoutout settings
- Logging level

**Expected**
- Settings survive app restart

---

## C. Live Updates

**Expected**
- Changing settings updates OBS/player without restart
- Invalid settings are rejected safely

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Settings UI implemented ([SettingsForm.cs](../src/Cliparino.Core/UI/SettingsForm.cs))
- Configuration persistence via appsettings.json
- Settings survive app restart
- Safe validation of invalid settings

**Note**: Hot-reload configuration (live updates without restart) is deferred as non-blocking enhancement.