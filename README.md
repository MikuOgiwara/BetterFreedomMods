# BetterFreedomMode

A standalone MelonLoader mod for SDEZ. It makes **Freedom Mode** usable on a single-screen cabinet,
and gives it back the systems the game takes away from it.

Tested on CiRCLE PLUS (`Ver.DX1.68-E`) and Magical Code, running alongside AquaMai.
The patched methods are identical across both, so there is no per-version code.

## What it does

| | |
|---|---|
| **Exit works** | The termination prompt opens on the monitor with no player entered — invisible on a 1P cabinet. It is moved to the visible one, and takes sole use of the buttons while open: **NEXT** confirms, **BACK** cancels, everything else goes dead so music select cannot start a song on the same press. |
| **Buttons light up** | While the prompt is open only its two answers are lit, in the game's own Yes/No colours. The rest go dark and come back exactly as they were. |
| **No forced last song** | Ending a credit used to force one more track, because the game reuses its selection-timeout handler. It now goes straight to the ending, via the map progress screen. |
| **Rewards land** | Freedom credits are reported to the server as normal play, so map distance, bonus tickets and event progress are awarded. |
| **Screens come back** | AREA SELECT, ticket select, the map/character select after a mid-credit unlock, and the map-bonus markers on songs. |
| **Character levels** | Freedom caps the levels one credit grants at 6. Configurable. |

## Install

Drop `BetterFreedomMode.dll` into the game's `Mods/` folder, next to `AquaMai.dll`. No known
conflict; AquaMai's infinite timer can stay on.

## Configuration

`UserData/MelonPreferences.cfg`, created on first launch.

```toml
[BetterFreedomMode]
ExitPromptOwnsTheButtons      = true   # prompt gets sole use of the buttons while open
DimLedsOnTerminationPrompt    = true   # light only NEXT and BACK, restore the rest on close
ShowConfirmationOnMainMonitor = true   # move the prompt to the visible monitor
ConfirmationMonitor           = 0      # which monitor that is
MapResultOnFreedomExit        = true   # show the km screen when exiting by Aime
ReportFreedomAsNormalPlay     = true   # report credits to the server as normal play
AreaSelectInFreedom           = true   # bring AREA SELECT back
EnableMapSystemInFreedom      = true   # map/character select + bonus markers
TicketSelectInFreedom         = true   # bring the ticket choice back
FreedomLevelBonusCap          = 99     # character levels one credit may grant; stock is 6
VerboseLogging                = false  # log what each patch does; on before reporting a problem
```

Two settings are worth a second look.

> **`FreedomLevelBonusCap` must not go above 99.** Past that the game does not cope with the level
> gain — found by testing, not from the source. Higher values are clamped to 99.

> **`ReportFreedomAsNormalPlay` trades away a record.** Playlogs stop recording which credits were
> played in Freedom Mode, and that cannot be recovered later. If you run your own server, the clean
> alternative is to treat `playMode == 1` as a normal credit in its reward rules and turn this off.

## Build

`Libs/` must hold these assemblies, with `Assembly-CSharp.dll` taken **from your own game install** —
a mismatched version makes Harmony fail to resolve the patches at load time. They are deliberately
absent from this repository, being game code.

```
Assembly-CSharp.dll   0Harmony.dll   MelonLoader.dll
UnityEngine.dll       UnityEngine.CoreModule.dll      mscorlib.dll
```

```sh
dotnet build -c Release          # -> Output/BetterFreedomMode.dll
```

To deploy into the game on every build, copy `Local.props.example` to `Local.props` and point it at
your `Mods/` folder. `Local.props` is gitignored.

## Notes

Each patch carries its reasoning in the doc comment above it — why that hook and not the obvious
one, and what broke when it was done differently. Worth reading before changing any of them; several
are shaped around traps that only show up on a real cabinet.

Two things are not done. The prompt's on-screen Yes/Cancel graphics live on the unrendered monitor
and would need reparenting to be seen; the physical lamps carry the same information for now. And
`CodeReadProcess` line 931 holds the same AREA SELECT test as the two patched sites, but inside a
coroutine, so it needs `AccessTools.EnumeratorMoveNext`; that path is only reachable through QR code
reading.

## Licence

No licence declared yet. This repository contains no game code or assets.
