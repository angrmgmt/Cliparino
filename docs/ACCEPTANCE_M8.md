# ACCEPTANCE_M8.md — Milestone 8: Release Readiness

Purpose:
Verify that Cliparino is shippable to end users.

---

## A. Preconditions
- M7 acceptance PASSED

---

## B. Diagnostics

**Expected**
- Logs rotate correctly
- Diagnostics bundle can be exported
- Secrets are redacted

---

## C. UX Baseline

**Expected**
- Tray app is discoverable
- No blocking dialogs during stream
- Clear failure messaging (logs or status)

---

## D. Clean Machine Test

**Expected**
- Fresh Windows machine
- Install → Connect Twitch → Connect OBS → Works

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- Structured logging with Serilog and rotating log files
- Diagnostics service ([DiagnosticsService.cs](../src/Cliparino.Core/Services/DiagnosticsService.cs))
- Token redaction in exports
- Tray application ([TrayApplicationContext.cs](../src/Cliparino.Core/UI/TrayApplicationContext.cs))
- Settings UI accessible from tray
- Update checker ([UpdateChecker.cs](../src/Cliparino.Core/Services/UpdateChecker.cs))
- No blocking dialogs during runtime
- All tests passing (4/4)
