# Discord Bot: Reaction Cleanup + Button System

**Date:** 2026-05-26  
**Project:** AlbionApp → LibEvents  
**Status:** Approved

---

## Overview

Two targeted changes to the LibEvents Discord integration. Both touch the same two files and are implemented in order as independent tasks.

**Tarea A — Reaction cleanup**  
When a user changes their role by reacting with a different emoji, the bot automatically removes the user's old reaction from the Discord message. Currently the old emoji stays visible, making it appear as though all slots are occupied.

**Tarea B — Button system**  
Add Discord message component buttons to every published event embed. Buttons use slot emojis as their content (compact, no long text). Slots disable globally when they reach capacity. A red ✖ button at the end allows any subscribed user to leave the event.

**Files changed:** `LibEvents/Discord/DiscordBotService.cs`, `LibEvents/Discord/EventEmbedHelper.cs`  
**No changes to:** entities, database schema, migrations, DTOs, or GatewayIntents.

---

## Tarea A — Reaction Cleanup

### Problem

`HandleReaction` updates the `EventParticipant` record in the database when a user switches roles, but does not remove the user's previous emoji reaction from the Discord message. The old emoji remains visible on the message, falsely indicating that role is occupied.

### Solution

Before overwriting the `EventParticipant` record, retrieve the user's current slot emoji and call `RemoveReactionAsync` on the message.

### Flow

```
ReactionAdded
  → find published GuildEvent by channelId + messageId
  → find BuildGroupSlot by emoji
  → if user already has an EventParticipant:
      load old BuildGroupSlot by participant.BuildId
      get IUserMessage from Discord
      await message.RemoveReactionAsync(new Emoji(oldSlot.Emoji), userId)
  → upsert EventParticipant (new buildId, new joinedAt)
  → RefreshEmbedAsync
```

### Notes

- `RemoveReactionAsync` requires the bot to have `ManageMessages` permission in the channel.
- The removal is best-effort: if it fails (e.g., message was deleted, permission revoked), log the error but do not abort the participant update.
- `ReactionRemoved` event (user manually removes their own reaction) maps to the "leave event" path already. No change needed there.

---

## Tarea B — Button System

### Design Constraints

Discord message components are **global** — one message is shared by all users in the channel. There is no per-user button state on a shared message. This means:

- Slot buttons are disabled when the slot reaches **maximum capacity** (global state).
- The ✖ button is **always visible** regardless of who is subscribed.
- The interaction handler enforces per-user logic server-side and responds with **ephemeral messages** visible only to the clicker.

### Button Layout (single row, ✖ at end)

```
[ 🗡️ ] [ 🛡️ ] [ 💚 ] [ ✖ ]
```

- Slot buttons: `ButtonStyle.Secondary` (gray), one per distinct slot emoji
- Full slot: `IsDisabled = true` (grayed out, non-interactive)
- ✖ button: `ButtonStyle.Danger` (red), always enabled, `CustomId = "slot:leave:{eventId}"`
- Discord.Net `ComponentBuilder` fills rows of 5 automatically; events with >4 slots wrap to a second row

### CustomId Scheme

| Button | CustomId |
|--------|----------|
| Slot join | `slot:join:{eventId}:{buildGroupSlotId}` |
| Leave event | `slot:leave:{eventId}` |

### `EventEmbedHelper` changes

Add a new static method alongside the existing `Build`:

```csharp
public static MessageComponent BuildComponents(GuildEvent ev, IEnumerable<EventParticipant> participants)
```

- Iterates `ev.BuildGroups.SelectMany(bg => bg.Slots)` for slot buttons
- Counts participants per slot to determine `IsDisabled`
- Appends the ✖ Danger button last
- Returns `ComponentBuilder.Build()`

The existing `Build(...)` signature is unchanged.

### `DiscordBotService` changes

**`StartAsync`:** subscribe `_client.InteractionCreated += HandleButtonInteraction`  
**`StopAsync`:** unsubscribe

**`HandleButtonInteraction(SocketInteraction interaction)`:**
1. Guard: `if (interaction is not SocketMessageComponent component) return`
2. `await component.DeferAsync(ephemeral: true)` — must happen within 3 seconds
3. Parse `component.Data.CustomId`:
   - `slot:join:{eventId}:{buildGroupSlotId}` → call `HandleSlotJoin`
   - `slot:leave:{eventId}` → call `HandleSlotLeave`
   - Unknown prefix → ignore
4. Each handler: update DB, call `RefreshEmbedAsync`, call `component.FollowupAsync(message, ephemeral: true)` with result feedback

**Ephemeral feedback messages:**

| Situation | Message |
|-----------|---------|
| Joined slot | "Te has inscrito como {emoji} ✅" |
| Already in that slot | "Ya estás inscrito en este rol." |
| Slot is full | "Este rol ya está lleno." |
| Left event | "Te has dado de baja del evento." |
| Not subscribed (leave) | "No estás inscrito en este evento." |
| Event not found / not published | (silent ignore) |

**`RefreshEmbedAsync`:** update `m.Components` alongside `m.Embed`:

```csharp
await message.ModifyAsync(m =>
{
    m.Embed = EventEmbedHelper.Build(ev, participants);
    m.Components = EventEmbedHelper.BuildComponents(ev, participants);
});
```

**`PublishEventAsync`:** pass components to the initial `SendMessageAsync` call.

**Footer text:** Change from `"Reacciona con el emoji de tu rol para inscribirte"` to `"Usa los botones o reacciona con el emoji de tu rol"` to reflect both interaction methods.

### GatewayIntents

No change required. `InteractionCreated` is delivered to the gateway regardless of intents.

---

## Error Handling

- Button interaction errors (DB failure, event not found): catch exception, log, respond ephemerally with "Algo salió mal, intenta de nuevo."
- `RemoveReactionAsync` failure: log warning, do not abort the participant update. The reaction cleanup is best-effort.
- Stale buttons (event ended, unpublished): interaction handler checks `ev.IsPublished`; if false, responds "Este evento ya no está activo."

---

## Out of Scope

- Per-user button state personalization (Discord limitation — not achievable on shared messages)
- Ephemeral follow-up interaction flows with personalized buttons
- Reaction-only mode fallback if buttons fail
- Slash commands for event management
