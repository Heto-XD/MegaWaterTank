# Mega Water Tank

Version: 0.14.0 · 2026-11-24

## What it is

A water tank that **starts at 0.1 t (100 L)** and grows through the expansion service up to 2.0 t.
It takes up to 16 insert cells that grow the same way (34.0 t in total when maxed out).
The water inside is **real**: hydroponics, purifiers and the water scanner all read it
through the vanilla rules, no extra setup needed.

## How to get one

Open the phone in the store. Your speed dial already has **Hetao Water Co. #21474836#** (added automatically even on a new game) —
tap it, and the last line opens the service page. Order from the "Water" tab:

| Service | Price | You get |
|---|---:|---|
| Mega Water Tank | 500 | One tank + one cell (200 L to start) |
| Extra Tank Cell | 250 | One cell (100 L to start) |
| **Expand Capacity** | **150** | +100 L for every container, 19 times (100 to 2000 L) |

Tanks and cells are **repeatable once a day**; **expansion has no cooldown** — buy it back to back. Deliveries go to the back room.

**Expand Capacity** works on your whole setup at once: each purchase makes your tank
**and every cell** 100 L larger — no need to upgrade them one by one. Nineteen purchases reach
the 2.0 t ceiling; the button then greys out and reads "Fully expanded to 2000 L".
Any container you obtain later already carries your current level.

> All 16 slots are free — the tank ships with them unlocked. You only pay for cells to fill them.

## How it behaves

- **Filling**: use the tank on a faucet, a bottle or another container, exactly like vanilla
- **Overflow**: once the tank is full, extra water flows into the empty cells
- **Refill**: below 90%, the tank pulls water back from its cells (checked about every 0.25 s)
- **Machine reserve**: when installed in a Moisture Farm's container slot, it frees space for
  the expected nightly output before the night settles (Auto mode, 25 L cap), then refills the
  main tank after production, so the tank reads full during the day
- **Inspect**: volume, quality and scanner results are shown by the vanilla rules (just like the vanilla water tank); this mod adds a "Total" line and a "Cells" line
- **Storage**: like the vanilla water tank, it fits into any container it physically fits in (no longer restricted to storage bays)

## What you will see

- **Mega Water Tank**: 2 cells wide, 3 cells tall, with a sight glass that changes as the level moves
- **Cell**: a 1-cell canister; slots into the tank, or works as a water bottle on its own

## Known limitations

- Pixel art (8x density, 11 level steps) that tints with water quality; the 16-slot look is still being checked in-game
- **Machine reserve only targets the Moisture Farm**: other machine slots keep the vanilla rules; set "Reserve amount" to 0 to turn it off completely
- Purifiers barely work on a tank this size — purify a small bottle first, then pour it in
- Cannot be sold to anyone (estimated value follows vanilla: empty tank 350, cell 25, plus 40 per litre of water; still flagged not-for-resale) to keep the economy intact
- **Do not throw a full tank into the trash**: its cells go with it

## Languages

Chinese (Simplified), English, Russian. Other languages fall back to English.

## Settings

In the main menu under **Modding → Mod settings**, there are two sections:

### Water automation

| Setting | Default | Effect |
|---|---|---|
| Reserve amount (L) | 25 | Upper bound for the water kept free for the Moisture Farm's nightly output; **0 = off**. The farm's hard limit is about 21.5 L/night. Numeric input; values outside 0–50 are clamped |
| Auto reserve from output | On | Reserves expected output + 0.5 L margin; off uses the fixed amount above |
| Keep reserve all day | Off | Off: reserve only before the night, then refill after production. On: keep it all day; the tank shows slightly less |

With Auto on, a normal 850 ml output reserves only about 1.35 L, so the tank does not stay visibly short. Changes apply on the next machine run/nightly settle; no restart needed.

### Diagnostics

| Setting | Default | Effect |
|---|---|---|
| Verbose log | Off | When on, writes init / patch-install / item-registration messages to `Player.log`; enable only when troubleshooting |

With it off the mod stays quiet during normal play. Warnings and errors are **always** logged regardless of this switch.

## License and origin

- **License: MPL 2.0** - source repository: `https://github.com/Heto-XD/MegaWaterTank`
- Most of the **code, artwork and text of this mod were produced with generative AI
  assistance**; everything was reviewed and play-tested by a human. See `AI-DISCLOSURE.md`
  in the repository for the scope and the copyright position.
- Unofficial community mod, not affiliated with the developer. No game assets are included
  and no third-party libraries are bundled.

## Uninstall

Delete the `Mods\megatank` folder. Existing tanks stay in saves as empty shells; saves are not corrupted.
