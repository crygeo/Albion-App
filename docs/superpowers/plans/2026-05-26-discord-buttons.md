# Discord Reaction Cleanup + Button System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix stale emoji reactions when users change roles, and add Discord message component buttons to every published event embed.

**Architecture:** Two independent tasks against `LibEvents/Discord/`. Task 1 (TDD) adds `BuildComponents` to `EventEmbedHelper` and creates a test project. Task 2 patches `HandleReaction` to auto-remove stale reactions. Task 3 wires the `InteractionCreated` handler into `DiscordBotService` and updates embed publishing.

**Tech Stack:** .NET 9, Discord.Net 3.15.3, EF Core SQLite 9.0.5, xUnit 2.9.3

**Working directory for all commands:** `C:\Users\z1705\RiderProjects\LibSolutions`

---

## File Map

| Action | Path |
|--------|------|
| Create | `LibEvents.Tests/LibEvents.Tests.csproj` |
| Create | `LibEvents.Tests/Discord/EventEmbedHelperTests.cs` |
| Modify | `LibEvents/Discord/EventEmbedHelper.cs` — add `BuildComponents`, update footer |
| Modify | `LibEvents/Discord/DiscordBotService.cs` — reaction cleanup + button handler |

---

## Task 1 — EventEmbedHelper: BuildComponents (TDD)

**Files:**
- Create: `LibEvents.Tests/LibEvents.Tests.csproj`
- Create: `LibEvents.Tests/Discord/EventEmbedHelperTests.cs`
- Modify: `LibEvents/Discord/EventEmbedHelper.cs`

---

- [ ] **Step 1: Create test project**

Create `LibEvents.Tests/LibEvents.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk"       Version="17.12.0" />
    <PackageReference Include="xunit"                        Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio"    Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\LibEvents\LibEvents.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Add test project to solution**

```bash
dotnet sln LibSolutions.sln add LibEvents.Tests/LibEvents.Tests.csproj
```

Expected output: `Project 'LibEvents.Tests\LibEvents.Tests.csproj' added to the solution.`

- [ ] **Step 3: Write the failing tests**

Create `LibEvents.Tests/Discord/EventEmbedHelperTests.cs`:

```csharp
using Discord;
using LibEvents.Discord;
using LibEvents.Entities;

namespace LibEvents.Tests.Discord;

public class EventEmbedHelperTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static GuildEvent MakeEvent(
        int eventId,
        params (int slotId, int buildId, string emoji, int quantity)[] slots)
    {
        var slotList = slots
            .Select((s, i) => new BuildGroupSlot
            {
                Id           = s.slotId,
                BuildGroupId = 1,
                BuildId      = s.buildId,
                Emoji        = s.emoji,
                Quantity     = s.quantity,
                SortOrder    = i,
            })
            .ToList<BuildGroupSlot>();

        return new GuildEvent
        {
            Id         = eventId,
            Name       = "Test Event",
            BuildGroup = new BuildGroup { Id = 1, Slots = slotList },
        };
    }

    private static List<ButtonComponent> GetButtons(MessageComponent components)
        => components.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .ToList();

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildComponents_NoParticipants_AllSlotButtonsEnabled()
    {
        var ev         = MakeEvent(1, (1, 10, "🗡️", 2), (2, 20, "🛡️", 1));
        var components = EventEmbedHelper.BuildComponents(ev, []);
        var buttons    = GetButtons(components);

        // 2 slot buttons + 1 leave button
        Assert.Equal(3, buttons.Count);
        Assert.False(buttons[0].IsDisabled); // 🗡️ — 0/2, not full
        Assert.False(buttons[1].IsDisabled); // 🛡️ — 0/1, not full
    }

    [Fact]
    public void BuildComponents_SlotAtCapacity_DisablesThatButton()
    {
        var ev = MakeEvent(1, (1, 10, "🗡️", 1), (2, 20, "🛡️", 2));
        var participants = new List<EventParticipant>
        {
            new() { EventId = 1, BuildId = 10, DiscordUserId = 111 },
        };

        var buttons = GetButtons(EventEmbedHelper.BuildComponents(ev, participants));

        Assert.True(buttons[0].IsDisabled);  // 🗡️ — 1/1, full
        Assert.False(buttons[1].IsDisabled); // 🛡️ — 0/2, not full
    }

    [Fact]
    public void BuildComponents_LeaveButton_NeverDisabled()
    {
        // Even when every slot is full, the ✖ leave button stays enabled.
        var ev = MakeEvent(1, (1, 10, "🗡️", 1));
        var participants = new List<EventParticipant>
        {
            new() { EventId = 1, BuildId = 10, DiscordUserId = 111 },
        };

        var buttons = GetButtons(EventEmbedHelper.BuildComponents(ev, participants));

        var leaveButton = buttons.Last();
        Assert.False(leaveButton.IsDisabled);
        Assert.Equal(ButtonStyle.Danger, leaveButton.Style);
    }

    [Fact]
    public void BuildComponents_CustomIds_EncodeEventAndSlotIds()
    {
        var ev      = MakeEvent(99, (42, 10, "💚", 1));
        var buttons = GetButtons(EventEmbedHelper.BuildComponents(ev, []));

        Assert.Equal("slot:join:99:42", buttons[0].CustomId);
        Assert.Equal("slot:leave:99",   buttons[1].CustomId);
    }

    [Fact]
    public void BuildComponents_NoSlots_OnlyLeaveButtonPresent()
    {
        var ev = new GuildEvent { Id = 5, Name = "Empty" }; // no BuildGroup
        var buttons = GetButtons(EventEmbedHelper.BuildComponents(ev, []));

        Assert.Single(buttons);
        Assert.Equal("slot:leave:5", buttons[0].CustomId);
    }
}
```

- [ ] **Step 4: Run tests — expect compile failure (BuildComponents not yet defined)**

```bash
dotnet test LibEvents.Tests/LibEvents.Tests.csproj --no-build 2>&1 | head -20
```

Expected: build error `'EventEmbedHelper' does not contain a definition for 'BuildComponents'`

- [ ] **Step 5: Implement BuildComponents and update footer in EventEmbedHelper**

Open `LibEvents/Discord/EventEmbedHelper.cs`. Make two changes:

**Change 1** — update the footer string on line 21:

```csharp
// OLD:
.WithFooter("Reacciona con el emoji de tu rol para inscribirte");

// NEW:
.WithFooter("Usa los botones o reacciona con el emoji de tu rol");
```

**Change 2** — add the new method after the closing brace of `Build` (before `GetSlotEmojis`):

```csharp
/// <summary>
/// Construye el <see cref="MessageComponent"/> con botones de slot para el evento.
/// Los botones de slot se deshabilitan cuando el slot llega a su capacidad máxima.
/// El botón ✖ (leave) siempre está habilitado.
/// </summary>
public static MessageComponent BuildComponents(
    GuildEvent ev,
    IEnumerable<EventParticipant> participants)
{
    var participantList = participants.ToList();
    var cb              = new ComponentBuilder();

    if (ev.BuildGroup?.Slots is { Count: > 0 } slots)
    {
        foreach (var slot in slots.OrderBy(s => s.SortOrder))
        {
            var count  = participantList.Count(p => p.BuildId == slot.BuildId);
            var isFull = count >= slot.Quantity;

            cb.WithButton(
                label:    slot.Emoji ?? "▪️",
                customId: $"slot:join:{ev.Id}:{slot.Id}",
                style:    ButtonStyle.Secondary,
                disabled: isFull,
                row:      0);
        }
    }

    cb.WithButton(
        label:    "✖",
        customId: $"slot:leave:{ev.Id}",
        style:    ButtonStyle.Danger,
        row:      0);

    return cb.Build();
}
```

The complete file after changes (`LibEvents/Discord/EventEmbedHelper.cs`):

```csharp
using Discord;
using LibEvents.Entities;

namespace LibEvents.Discord;

/// <summary>
/// Construye el <see cref="Embed"/> y el <see cref="MessageComponent"/> de Discord
/// para un <see cref="GuildEvent"/>.
/// Separado de <see cref="DiscordBotService"/> para poder testearlo sin cliente real.
/// </summary>
public static class EventEmbedHelper
{
    private const uint ColorCyan = 0x00B4D8;

    public static Embed Build(GuildEvent ev, IEnumerable<EventParticipant> participants)
    {
        var participantList = participants.ToList();

        var builder = new EmbedBuilder()
            .WithTitle($"📅  {ev.Name}")
            .WithColor(new Color(ColorCyan))
            .WithFooter("Usa los botones o reacciona con el emoji de tu rol");

        if (!string.IsNullOrWhiteSpace(ev.Description))
            builder.WithDescription(ev.Description);

        // Fecha/hora
        if (ev.ScheduledAt is { } dt)
        {
            var unix = new DateTimeOffset(dt, TimeSpan.Zero).ToUnixTimeSeconds();
            builder.AddField("🕐  Hora (UTC)", $"<t:{unix}:F>", inline: false);
        }

        // Composición con conteo de confirmados
        if (ev.BuildGroup?.Slots is { Count: > 0 } slots)
        {
            var lines = slots
                .OrderBy(s => s.SortOrder)
                .Select(s =>
                {
                    var members = participantList.Where(p => p.BuildId == s.BuildId).ToList();
                    var emoji   = s.Emoji ?? "▪️";
                    var name    = s.Build?.Name ?? "—";
                    var names   = members.Count > 0
                        ? "  —  " + string.Join(", ", members.Select(p => "@" + p.DiscordUsername))
                        : "";
                    return $"{emoji}  **{name}**  —  {members.Count}/{s.Quantity}{names}";
                });

            builder.AddField("⚔️  Composición", string.Join("\n", lines), inline: false);
        }

        return builder.Build();
    }

    /// <summary>
    /// Construye el <see cref="MessageComponent"/> con botones de slot para el evento.
    /// Los botones de slot se deshabilitan cuando el slot llega a su capacidad máxima.
    /// El botón ✖ (leave) siempre está habilitado.
    /// </summary>
    public static MessageComponent BuildComponents(
        GuildEvent ev,
        IEnumerable<EventParticipant> participants)
    {
        var participantList = participants.ToList();
        var cb              = new ComponentBuilder();

        if (ev.BuildGroup?.Slots is { Count: > 0 } slots)
        {
            foreach (var slot in slots.OrderBy(s => s.SortOrder))
            {
                var count  = participantList.Count(p => p.BuildId == slot.BuildId);
                var isFull = count >= slot.Quantity;

                cb.WithButton(
                    label:    slot.Emoji ?? "▪️",
                    customId: $"slot:join:{ev.Id}:{slot.Id}",
                    style:    ButtonStyle.Secondary,
                    disabled: isFull,
                    row:      0);
            }
        }

        cb.WithButton(
            label:    "✖",
            customId: $"slot:leave:{ev.Id}",
            style:    ButtonStyle.Danger,
            row:      0);

        return cb.Build();
    }

    /// <summary>
    /// Devuelve los emojis únicos (unicode) de los builds del evento,
    /// para añadirlos como reacciones al mensaje.
    /// </summary>
    public static IEnumerable<string> GetSlotEmojis(GuildEvent ev)
        => ev.BuildGroup?.Slots
               .Where(s => !string.IsNullOrWhiteSpace(s.Emoji))
               .Select(s => s.Emoji!)
               .Distinct()
           ?? [];
}
```

- [ ] **Step 6: Run tests — expect all 5 to pass**

```bash
dotnet test LibEvents.Tests/LibEvents.Tests.csproj -v minimal
```

Expected output:
```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

- [ ] **Step 7: Commit**

```bash
git add LibEvents.Tests/ LibEvents/Discord/EventEmbedHelper.cs
git commit -m "feat(LibEvents): add BuildComponents to EventEmbedHelper + tests

- Builds MessageComponent with one Secondary button per slot (disabled when full)
- Adds Danger ✖ leave button at end of row
- Updates embed footer to mention both interaction methods
- Adds LibEvents.Tests project with 5 unit tests

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Task 2 — Reaction Cleanup in HandleReaction (Tarea A)

**Files:**
- Modify: `LibEvents/Discord/DiscordBotService.cs` — `HandleReaction` method (lines 202–271)

---

- [ ] **Step 1: Add RemoveReactionAsync call inside HandleReaction**

In `DiscordBotService.cs`, locate the `if (previous.Count > 0)` block (currently lines 233–237):

```csharp
// CURRENT:
if (previous.Count > 0)
{
    db.EventParticipants.RemoveRange(previous);
    await db.SaveChangesAsync();
}
```

Replace with:

```csharp
if (previous.Count > 0)
{
    // Tarea A: remove the user's old reaction so the slot doesn't appear
    // occupied to other users after a role switch.
    if (previous[0].BuildId is { } oldBuildId)
    {
        var oldSlot = ev.BuildGroup?.Slots
            .FirstOrDefault(s => s.BuildId == oldBuildId);

        if (oldSlot?.Emoji is { } oldEmoji
            && ev.DiscordChannelId.HasValue
            && ev.DiscordMessageId.HasValue)
        {
            try
            {
                var ch = await _client!.GetChannelAsync(ev.DiscordChannelId.Value)
                             as ITextChannel;
                if (ch is not null
                    && await ch.GetMessageAsync(ev.DiscordMessageId.Value)
                           is IUserMessage msg)
                {
                    await msg.RemoveReactionAsync(new Emoji(oldEmoji), reaction.UserId);
                }
            }
            catch (Exception ex)
            {
                // Best-effort: log but never abort the participant update.
                Console.Error.WriteLine(
                    $"[DiscordBotService] RemoveReactionAsync failed: {ex.Message}");
            }
        }
    }

    db.EventParticipants.RemoveRange(previous);
    await db.SaveChangesAsync();
}
```

- [ ] **Step 2: Build to verify no compile errors**

```bash
dotnet build LibEvents/LibEvents.csproj -v minimal
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add LibEvents/Discord/DiscordBotService.cs
git commit -m "fix(LibEvents): remove stale emoji reaction on role switch

When a user reacts with a different emoji to change their role, the bot
now calls RemoveReactionAsync on the old emoji before updating the DB.
The call is best-effort: failures are logged but never abort the update.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Task 3 — Button Interaction Handler (Tarea B)

**Files:**
- Modify: `LibEvents/Discord/DiscordBotService.cs` — `StartAsync`, `StopAsync`, `RefreshEmbedAsync`, `PublishEventAsync`, plus 3 new private methods

---

- [ ] **Step 1: Subscribe InteractionCreated in StartAsync / StopAsync**

In `StartAsync`, add after the existing reaction subscriptions (lines 70–71):

```csharp
// EXISTING (context):
_client.ReactionAdded   += OnReactionAdded;
_client.ReactionRemoved += OnReactionRemoved;

// ADD:
_client.InteractionCreated += OnInteractionCreated;
```

In `StopAsync`, add after the existing reaction unsubscriptions (lines 84–85):

```csharp
// EXISTING (context):
_client.ReactionAdded   -= OnReactionAdded;
_client.ReactionRemoved -= OnReactionRemoved;

// ADD:
_client.InteractionCreated -= OnInteractionCreated;
```

- [ ] **Step 2: Add OnInteractionCreated dispatcher**

Add after the `OnReactionRemoved` method (after line ~200):

```csharp
private async Task OnInteractionCreated(SocketInteraction interaction)
{
    if (!_running) return;
    if (interaction is not SocketMessageComponent component) return;

    // Discord requires acknowledgement within 3 seconds.
    await component.DeferAsync(ephemeral: true);

    var parts = component.Data.CustomId.Split(':');
    if (parts.Length < 3) return;

    await using var db = await _factory.CreateDbContextAsync();

    switch ($"{parts[0]}:{parts[1]}")
    {
        case "slot:join" when parts.Length == 4
                           && int.TryParse(parts[2], out var joinEventId)
                           && int.TryParse(parts[3], out var joinSlotId):
            await HandleButtonJoinAsync(component, db, joinEventId, joinSlotId);
            break;

        case "slot:leave" when parts.Length == 3
                            && int.TryParse(parts[2], out var leaveEventId):
            await HandleButtonLeaveAsync(component, db, leaveEventId);
            break;
    }
}
```

- [ ] **Step 3: Add HandleButtonJoinAsync**

Add after `OnInteractionCreated`:

```csharp
private async Task HandleButtonJoinAsync(
    SocketMessageComponent component,
    EventsDbContext        db,
    int                    eventId,
    int                    slotId)
{
    var ev = await db.GuildEvents
        .Include(e => e.BuildGroup)
            .ThenInclude(g => g!.Slots)
                .ThenInclude(s => s.Build)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (ev is null || !ev.IsPublished)
        return;

    var slot = ev.BuildGroup?.Slots.FirstOrDefault(s => s.Id == slotId);
    if (slot is null)
    {
        await component.FollowupAsync("Este slot ya no existe.", ephemeral: true);
        return;
    }

    var participants = await db.EventParticipants
        .Where(p => p.EventId == eventId)
        .ToListAsync();

    var existing = participants.FirstOrDefault(p => p.DiscordUserId == component.User.Id);

    // Already in this exact slot — nothing to do.
    if (existing?.BuildId == slot.BuildId)
    {
        await component.FollowupAsync("Ya estás inscrito en este rol.", ephemeral: true);
        return;
    }

    // Capacity check (re-read in case it changed between button render and click).
    var occupiedCount = participants.Count(p => p.BuildId == slot.BuildId);
    if (occupiedCount >= slot.Quantity)
    {
        await component.FollowupAsync("Este rol ya está lleno.", ephemeral: true);
        return;
    }

    // Remove previous role (role switch via button).
    if (existing is not null)
        db.EventParticipants.Remove(existing);

    db.EventParticipants.Add(new EventParticipant
    {
        EventId         = eventId,
        DiscordUserId   = component.User.Id,
        DiscordUsername = component.User.Username,
        BuildId         = slot.BuildId,
        JoinedAt        = DateTime.UtcNow,
    });
    await db.SaveChangesAsync();

    await RefreshEmbedAsync(ev, db);
    ParticipationChanged?.Invoke(ev.Id);

    await component.FollowupAsync($"Te has inscrito como {slot.Emoji ?? "▪️"} ✅", ephemeral: true);
}
```

- [ ] **Step 4: Add HandleButtonLeaveAsync**

Add after `HandleButtonJoinAsync`:

```csharp
private async Task HandleButtonLeaveAsync(
    SocketMessageComponent component,
    EventsDbContext        db,
    int                    eventId)
{
    var ev = await db.GuildEvents
        .Include(e => e.BuildGroup)
            .ThenInclude(g => g!.Slots)
                .ThenInclude(s => s.Build)
        .FirstOrDefaultAsync(e => e.Id == eventId);

    if (ev is null || !ev.IsPublished)
        return;

    var existing = await db.EventParticipants
        .FirstOrDefaultAsync(p => p.EventId      == eventId
                               && p.DiscordUserId == component.User.Id);

    if (existing is null)
    {
        await component.FollowupAsync("No estás inscrito en este evento.", ephemeral: true);
        return;
    }

    db.EventParticipants.Remove(existing);
    await db.SaveChangesAsync();

    await RefreshEmbedAsync(ev, db);
    ParticipationChanged?.Invoke(ev.Id);

    await component.FollowupAsync("Te has dado de baja del evento.", ephemeral: true);
}
```

- [ ] **Step 5: Update RefreshEmbedAsync to include components**

Replace the current `RefreshEmbedAsync` body (lines 273–285):

```csharp
// CURRENT:
private async Task RefreshEmbedAsync(GuildEvent ev, EventsDbContext db)
{
    if (ev.DiscordMessageId is null || ev.DiscordChannelId is null) return;

    var participants = await db.EventParticipants
        .Where(p => p.EventId == ev.Id)
        .ToListAsync();

    if (await _client!.GetChannelAsync(ev.DiscordChannelId.Value) is not ITextChannel ch) return;
    if (await ch.GetMessageAsync(ev.DiscordMessageId.Value) is not IUserMessage msg) return;

    await msg.ModifyAsync(p => p.Embed = EventEmbedHelper.Build(ev, participants));
}
```

```csharp
// NEW:
private async Task RefreshEmbedAsync(GuildEvent ev, EventsDbContext db)
{
    if (ev.DiscordMessageId is null || ev.DiscordChannelId is null) return;

    var participants = await db.EventParticipants
        .Where(p => p.EventId == ev.Id)
        .ToListAsync();

    if (await _client!.GetChannelAsync(ev.DiscordChannelId.Value) is not ITextChannel ch) return;
    if (await ch.GetMessageAsync(ev.DiscordMessageId.Value) is not IUserMessage msg) return;

    await msg.ModifyAsync(p =>
    {
        p.Embed      = EventEmbedHelper.Build(ev, participants);
        p.Components = EventEmbedHelper.BuildComponents(ev, participants);
    });
}
```

- [ ] **Step 6: Update PublishEventAsync to send components with initial message**

Replace the `SendMessageAsync` call in `PublishEventAsync` (around line 134):

```csharp
// CURRENT:
var embed   = EventEmbedHelper.Build(ev, participants);
var message = await channel.SendMessageAsync(text: "@everyone", embed: embed);
```

```csharp
// NEW:
var embed      = EventEmbedHelper.Build(ev, participants);
var components = EventEmbedHelper.BuildComponents(ev, participants);
var message    = await channel.SendMessageAsync(
    text:       "@everyone",
    embed:      embed,
    components: components);
```

- [ ] **Step 7: Build everything to verify no compile errors**

```bash
dotnet build LibEvents/LibEvents.csproj -v minimal
dotnet test  LibEvents.Tests/LibEvents.Tests.csproj -v minimal
```

Expected:
```
Build succeeded.
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

- [ ] **Step 8: Commit**

```bash
git add LibEvents/Discord/DiscordBotService.cs
git commit -m "feat(LibEvents): add Discord message component button system

- Subscribe InteractionCreated in StartAsync/StopAsync
- OnInteractionCreated: DeferAsync + route slot:join / slot:leave
- HandleButtonJoinAsync: capacity check, role-switch, ephemeral feedback
- HandleButtonLeaveAsync: remove participant, ephemeral feedback
- RefreshEmbedAsync: now updates both Embed and Components
- PublishEventAsync: sends components with initial message

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```
