# BetterFreedomMode

A standalone MelonLoader mod for SDEZ. It makes **Freedom Mode** usable on a
single-screen cabinet, and gives it back the systems the game takes away from it.

Tested on `Ver.DX1.68-E`, running alongside AquaMai.

---

## Why

Stock Freedom Mode has three problems on a home cabinet, and strips a lot out on any cabinet.

**You cannot leave.** To end a credit the game wants you to tap an Aime and then press **button 4**.
That whole flow (`PleaseWaitProcess`) runs on the monitor with *no player entered* — on a 1P
cabinet that is the 2P side: the window opens on a screen nobody sees and waits for buttons that are
not wired up.

**Leaving forces one more song.** `ForcedTerminationForFreedomMode()` only zeroes the clock.
`MusicSelectProcess` then sees `GetFreedomModeMSec() <= 0` and calls `TimerCountUp()` — the
*selection timeout* handler, which force-picks the highlighted song and ends in `OnGameStart()`.

**Rewards do not land.** Map distance (km), character XP, bonus tickets, event progress.

---

## What the mod does

Every block is independent and can be switched off.

### 1. Any button confirms — `FreedomExitAnyButton.cs`

While the confirmation window is open, buttons 1, 2, 3, 6, 7 and 8 — **on either side** — are
reported to the game as button 4. Button 5 is relayed the same way so the stock cancel stays
reachable from the 1P side. `Select` is excluded (service button), and the game's own 500 ms input
lock still prevents a confirmation in the same frame as the Aime tap.

### 2. Confirmation on the visible monitor — `FreedomConfirmOnMainMonitor.cs`

Moves the `FreedomModeTerminationMessage` window from monitor 1 to monitor 0, and undoes the
redirect on the matching `CloseWindow` — otherwise the window would never close.

### 3. Exit without the forced song — `FreedomImmediateExit.cs`

Prefix on `MusicSelectProcess.TimerCountUp`: instead of the forced selection, it goes straight to
the stock ending screen. Three traps had to be defused, each found by a crash or a lock-in during
play — see [Implementation notes](#implementation-notes).

### 4. Credits reported as normal play — `FreedomReportAsNormalPlay.cs`

The reward systems are **not disabled in the client** — the client is only a display layer for them.
`UpdateTotalAwake()` merely re-sums `CharaList[].Awakening`, and the only `Distance +=` in the whole
game is in `PlInformationProcess`, a debug tool. The real values come from the server, which keys
off the playlog:

```csharp
result.playMode = 1;          // ExportUserGamePlaylog and ExportUserPlaylog
result.isFreedomMode = true;  // ExportUserPlaylog
```

So this reports Freedom credits as ordinary ones. The credit cost (`GameCostEnoughFreedom`) is left
truthful: it feeds bookkeeping, not rewards.

> **Deliberate trade-off.** Playlogs no longer record which credits were played in Freedom Mode, and
> that cannot be recovered afterwards. If you run your own server, the clean alternative is to treat
> `playMode == 1` as a normal credit in its reward rules and leave this block off.

### 5. AREA SELECT — `FreedomAreaSelect.cs`

`SimpleSettingProcess → LoginBonusProcess → RegionalSelectProcess` is the normal credit-start path.
In Freedom Mode a ternary routes it to `GetPresentProcess` instead. The mod brings the area select
screen back. Presents are not lost: `GetPresentProcess` already runs earlier via
`UnlockMusicProcess`.

### 6. Map system — `FreedomMapSystem.cs`

Restores the map/character select screens after a mid-credit unlock, and the map-bonus markers on
songs. The km progress screen shows **once, at the end of the credit** — stock Freedom Mode never
shows it at all, `ResultProcess` skips it on every track including the last.

### 7. Ticket select — `FreedomTicketSelect.cs`

`TicketSelectMonitor.Initialize` auto-answers the screen in Freedom Mode
(`_decidedSelectTicketID = 0`). The mod gives you a real choice.

---

## Install

Drop `BetterFreedomMode.dll` into the game's `Mods/` folder, next to `AquaMai.dll`.

No known conflict with AquaMai: it only patches `GameManager.GetFreedomStartTime`, the
`IsFreedomTimerPause` getter, `ModeSelectMonitor.Initialize` and, for SinglePlayer,
`PleaseWaitProcess.OnStart` / `PleaseWaitMonitor.Initialize`. AquaMai's infinite timer can stay on.

## Configuration

`UserData/MelonPreferences.cfg`, created on first launch:

```toml
[BetterFreedomMode]
CancelButtonConfirms          = false  # true: button 5 confirms instead of cancelling
ShowConfirmationOnMainMonitor = true   # move the confirmation window
ConfirmationMonitor           = 0      # destination monitor
MapResultOnFreedomExit        = true   # km screen when exiting by Aime
ReportFreedomAsNormalPlay     = true   # report credits to the server as normal play
AreaSelectInFreedom           = true   # bring AREA SELECT back
EnableMapSystemInFreedom      = true   # map/character select + bonus markers
TicketSelectInFreedom         = true   # bring the ticket choice back
```

## Build

`Libs/` must hold these assemblies, with `Assembly-CSharp.dll` taken **from your own game install** —
a mismatched version makes Harmony fail to resolve the patches at load time:

```
Assembly-CSharp.dll   0Harmony.dll   MelonLoader.dll
UnityEngine.dll       UnityEngine.CoreModule.dll      mscorlib.dll
```

They are deliberately absent from this repository (game code, copyrighted).

```sh
dotnet build -c Release          # -> Output/BetterFreedomMode.dll
```

To deploy into the game on every build, copy `Local.props.example` to `Local.props` and set your
`Mods/` folder there. `Local.props` is gitignored.

---

## Implementation notes

Things learned in-game that explain why the code is shaped the way it is.

### Mono inlines small accessors — sometimes

`GameManager.IsFreedomMapSkip()` is two lines long. A call counter installed by a Harmony postfix
reported **0 calls** across one credit and **1 call** across the next. Mono inlines these methods
into some callers, and an inlined caller never reaches the Harmony detour — silently, with no error.

The consequence is that patching a getter is not reliable. Where the answer genuinely has to change
(`FreedomAreaSelect`, `FreedomTicketSelect`), the mod **writes the field** through the public setter
on `IsFreedomMode` for the duration of one method and restores it afterwards — an inlined getter
compiles down to a read of that same backing field.

### Restore from a finalizer, never a postfix

A postfix is skipped when the patched method throws. Leaving `IsFreedomMode` false would silently
disable everything else. Every restore therefore runs from `[HarmonyFinalizer]`, which executes
either way. `FreedomFlagScope` centralises this with a **shared** depth counter: separate counters
per feature would let an inner window re-enable the flag while an outer one was still running.

### Enumerate before concluding

The reach of `IsFreedomMapSkip` was first inferred from a `grep | head -40` that had been truncated.
The consumers cut off (`ResultProcess:1300`, `UnlockProcess:117`) were exactly the ones controlling
the map screen. The patch is now a **whitelist** — the override applies only inside explicitly
chosen callers — rather than a blacklist.

| Caller | Role | Answer |
|---|---|---|
| `NextTrackProcess.OnStart` 210/214 | map/character select after an unlock | `false` |
| `NotesListManager` 96 | map-bonus markers | `false` |
| `NextTrackProcess` 342 | pauses the Freedom clock between tracks | `true` |
| `ResultProcess` 1300 | skips the map result screen | `true`, except on the last track |
| `UnlockProcess` 117 | same, on the unlock path | `true` |

### The `MusicTrackNumber` invariant

The whole ending chain assumes `MusicTrackNumber` equals the number of **finished** tracks. The
`NextTrackProcess` following a track has already incremented it for the next selection, so it runs
one ahead of the score log. Two consumers read past the end:

- `NextTrackProcess.CheckAchieveTrack()` reads index `MusicTrackNumber - 1` → `GetGameScore` returns
  `null` and `GetGhostScore` dereferences it.
- `PhotoEditProcess.OnStart()` sizes its arrays from `GetMaxTrackCount()` and fills them from the
  shorter log.

The mod restores the invariant at the point of divergence rather than patching each consumer. With
**zero tracks played** it declines the shortcut and lets the stock forced song run:
`AchieveCreditData.Create` calls `.Min()` over an empty sequence.

### The exit trap

Exiting after exactly one track used to lock the player in, through two combined mechanisms:

1. `NextTrackProcess.OnStart` switches to `NeedAwake` mode — which literally does
   `AddProcess(new MusicSelectProcess(...))` — whenever a character awakening has neither been
   played nor been queued. In a stock Freedom credit the clock expires *during* a track, so that
   track's `MapResult` plays the awakening; an Aime exit happens afterwards, with `IsFreedomTimeUp`
   still false.
2. `MusicSelectProcess` restarts the credit with
   `if (!IsFreedomCountDown && MusicTrackNumber == 1) StartFreedomModeTimer(...)`, which clears
   `IsFreedomTimeUp`. The rewind above satisfied the second condition.

The mod marks the awakening as handled and refuses any clock restart while the credit is ending,
behind a flag that only clears on `GameManager.Clear()` — the real credit boundary.

---

## Landmarks in the game code

| Thing | Location |
|---|---|
| Forced-termination state machine | `Process/PleaseWaitProcess.cs:140-176` |
| Aime tap → window opens | `Process/PleaseWaitProcess.cs:319` |
| Clock, flags, forced termination | `Manager/GameManager.cs` |
| Forced selection on timeout | `Process/MusicSelectProcess.cs:2182`, `TimerCountUp` |
| `IsFreedomTimeUp` consumers | `Process/NextTrackProcess.cs:OnStart` |
| Flags sent to the server | `Net.VO.Mai2/VOExtensions.cs:1155`, `1666-1668` |
| AREA SELECT ternaries | `CircleFinalResultProcess.cs:283`, `LoginBonusProcess.cs:204` |

### Known limitation

`CodeReadProcess.cs:931` carries the same AREA SELECT ternary, but inside `TimeUpCoroutine()`. A
coroutine body runs in a compiler-generated `MoveNext()` rather than in the method Harmony would
patch; it would need `AccessTools.EnumeratorMoveNext`. That path is only reachable when
`IsGotoCodeRead` is set (QR code reading), so it is left alone.

## Licence

No licence declared yet. This repository contains no game code or assets.
