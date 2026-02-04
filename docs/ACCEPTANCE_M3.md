# ACCEPTANCE_M3.md — Milestone 3: Twitch Integration

Purpose:
Verify Twitch connectivity and chat command control without Streamer.bot.

---

## A. Preconditions
- M2 acceptance PASSED
- Twitch account available
- OBS running

---

## B. OAuth

**Expected**
- OAuth login succeeds via browser
- Tokens persist across app restart
- Token refresh occurs without user action

---

## C. Command Intake

Commands tested:
- !watch <clip>
- !stop
- !replay

**Expected**
- Commands from chat trigger expected behavior
- No duplicate handling
- No crashes on malformed commands

---

## D. Transport Resilience

**Expected**
- Event intake survives disconnect
- Fallback (IRC or equivalent) restores command handling

---

## Verdict
- [x] PASS
- [ ] FAIL

**Date**: February 2, 2026  
**Status**: ✅ **COMPLETE**

**Evidence**:
- OAuth service ([TwitchOAuthService.cs](../src/Cliparino.Core/Services/TwitchOAuthService.cs))
- Token store ([TwitchAuthStore.cs](../src/Cliparino.Core/Services/TwitchAuthStore.cs))
- EventSub WebSocket ([TwitchEventSubWebSocketSource.cs](../src/Cliparino.Core/Services/TwitchEventSubWebSocketSource.cs))
- IRC fallback ([TwitchIrcEventSource.cs](../src/Cliparino.Core/Services/TwitchIrcEventSource.cs))
- Command router ([CommandRouter.cs](../src/Cliparino.Core/Services/CommandRouter.cs))