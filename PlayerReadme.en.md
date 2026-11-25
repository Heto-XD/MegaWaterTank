# Mega Water Tank

Version: 0.16.0 · 2026-11-25

## What it is

A water tank that **starts at 0.1 t (100 L)** and grows through the expansion service up to **2.0 t** for the main tank and every cell.

- Main grid: **4×6 = 24 insert cells**
- Right side: **2×2 = 4 filter slots**
- Fully loaded (tank + 24 cells): **50,000 L (50 t)**

The water inside is **real**: hydroponics, purifiers and the water scanner all read it through the vanilla rules, no extra setup needed.

## How to get one

Open the phone in the store. Your speed dial already has **Hetao Water Co. #21474836#** (added automatically even on a new game) — tap it and **order right in the call**. The menu has three options; after a purchase you land back on the menu, so you can keep buying. Hanging up: the phone's red button (or the "End the call" option).

| Item | Price | You get |
|---|---:|---|
| Mega Water Tank | 500 | One tank + one cell (200 L to start) |
| Extra Tank Cell | 250 | One cell (100 L to start) |
| **Expand Capacity** | **150** | +100 L for every container, 19 times (100 to 2000 L) |

Tanks and cells are **one per day** (it resets next morning; the option greys out and says "already bought today" until then). **Expansion has no cooldown** — buy it back to back. Options you cannot afford are greyed out too. Deliveries go to the back room.

Prices come out of **cash plus Wild favours**, favours first — the same rule the vanilla Wild network uses.

**Expand Capacity** works on your whole setup at once: each purchase makes your tank **and every cell** 100 L larger — no need to upgrade them one by one. Nineteen purchases reach the 2.0 t ceiling; the option then reads "already at the 2000 L ceiling" and greys out.
Any container you obtain later already carries your current level.

> All 24 slots are free — the tank ships with them unlocked. You only pay for cells to fill them.

## How it behaves

### Machine reserve (default)

- **Filling**: use the tank on a faucet, a bottle or another container, exactly like vanilla.
- **Overflow**: once the tank is full, extra water flows into the empty cells; pouring from a bottle/jug is covered too.
- **Refill**: below 90%, the tank pulls water back from its cells (about every 0.25 s).
- **Machine reserve**: when installed in a Moisture Farm's container slot, it frees space for the expected nightly output before the night settles (Auto mode, 25 L cap), then refills after production.

### Reservoir mode (optional)

Turn on "Reservoir mode" in the settings:

- above the target +5%: excess water is moved into the cells;
- below the target -5%: water is pulled back into the main tank;
- target is **0–100%**, default **90%**;
- reservoir mode disables the vanilla 90% refill and machine reserve, so the two policies do not fight.

> Reservoir mode is global: all Mega Water Tanks share one target. 40–80% is recommended; above 90% the main tank has little headroom.

### Filters

The right 2×2 area holds **4 filter cartridges**:

- normal, advanced, hydroreed, kotton and other 1-cell filters are accepted;
- order is **top-left → top-right → bottom-left → bottom-right**; the next usable filter takes over when one is depleted;
- `uv_filter` is rejected;
- filters only clean water **entering the main tank** (faucet, bottle, machine transfer); reservoir/cell circulation is not filtered.

### Other

- **Storage**: like the vanilla water tank, it fits into any container it physically fits in.
- **Old saves**: cells in the old 4×4 area keep their coordinates; rows 5/6 are new. Filter slots are new.

## What you will see

- **Mega Water Tank**: 2 cells wide, 3 cells tall, with a sight glass that changes as the level moves.
- **Cell**: a 1-cell canister; slots into the tank, or works as a water bottle on its own.
- **Filter slots**: four independent slots on the right, using the advanced filter background.

## Known limitations

- Pixel art (8x density, 11 level steps) that tints with water quality; the 24-slot look is still being checked in-game.
- **Target 0% empties the main tank**; consumers that only read the main pool (hydroponics, purifiers) will see an empty tank. **100% leaves no room for machines.**
- At the default 90%, a 100 L tank has only 10 L headroom; the Moisture Farm's extreme 21.47 L/night may not fit. Use 40–80%.
- **Stacked cells are skipped by reservoir/overflow**: split them into single cells; the log says so.
- Filters only clean main-tank inflow; reservoir movement does not purify old dirty water.
- Purifiers barely work on a tank this size — purify a small bottle first, then pour it in.
- Cannot be sold to anyone (to keep the economy intact).
- **Do not throw a full tank into the trash**: its cells go with it.

## Languages

Chinese (Simplified), English, Russian. Other languages fall back to English.

## Settings

In the main menu under **Modding → Mod settings**, there are two sections:

### Water automation

| Setting | Default | Effect |
|---|---|---|
| Reservoir mode | Off | On: keep the main tank near the target; Off: use machine reserve |
| Reservoir target (%) | 90 | 0–100 numeric input; 40–80 recommended; no movement within ±5% |
| Reserve amount (L) | 25 | Machine-reserve upper bound; **0 = off**. Numeric input, clamped to 0–50 |
| Auto reserve from output | On | Reserve expected output + 0.5 L margin; off uses the fixed amount above |
| Keep reserve all day | Off | Off: reserve before the night, refill after production. On: keep it all day |

- In reservoir mode, `Reserve amount / Auto / Keep all day` do not apply;
- In machine-reserve mode, `Reservoir mode / Reservoir target` do not apply;
- Changes apply on the next machine run/nightly settle; no restart needed.

### Diagnostics

| Setting | Default | Effect |
|---|---|---|
| Verbose log | Off | When on, writes init / patch-install / item-registration / reservoir summary messages to `Player.log`; enable only when troubleshooting |

With it off the mod stays quiet during normal play. Warnings and errors are **always** logged regardless of this switch.

## License and origin

- **License: MPL 2.0** - source repository: `https://github.com/Heto-XD/MegaWaterTank`
- Most of the **code, artwork and text of this mod were produced with generative AI assistance**; everything was reviewed and play-tested by a human. See `AI-DISCLOSURE.md` in the repository for the scope and the copyright position.
- Unofficial community mod, not affiliated with the developer. No game assets are included and no third-party libraries are bundled.

## Uninstall

Delete the `Mods\megatank` folder. Existing tanks stay in saves as empty shells; saves are not corrupted.
