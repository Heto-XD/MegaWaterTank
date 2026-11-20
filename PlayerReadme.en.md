# Mega Water Tank

Version: 0.8.1 · 2026-11-20

## What it is

A 1.9 t water tank that takes up to 16 insert cells of the same size (32.3 t total).
The water inside is **real**: hydroponics, purifiers and the water scanner all read it
through the vanilla rules, no extra setup needed.

## How to get one

Open the phone in the store. Your speed dial already has **Hetao Water Co. #21474836#** (added automatically even on a new game) —
tap it, and the last line opens the service page. Order from the "Water" tab:

| Service | Price | You get |
|---|---:|---|
| Mega Water Tank | 500 | One tank + one cell (3.8 t) |
| Extra Tank Cell | 250 | One cell (1.9 t) |

Both are **repeatable**: bought today, available again tomorrow. Deliveries go to the back room.

> All 16 slots are free — the tank ships with them unlocked. You only pay for cells to fill them.

## How it behaves

- **Filling**: use the tank on a faucet, a bottle or another container, exactly like vanilla
- **Overflow**: once the tank is full, extra water flows into the empty cells
- **Refill**: below 90%, the tank pulls water back from its cells (checked about every 0.25 s)
- **Inspect**: hover for exact millilitres and the water grade; use a **water scanner** for contents

## What you will see

- **Mega Water Tank**: 2 cells wide, 3 cells tall, with a sight glass that changes as the level moves
- **Cell**: a 1-cell canister; slots into the tank, or works as a water bottle on its own

## Known limitations

- Pixel art (8x density, 11 level steps) that tints with water quality; the 16-slot look is still being checked in-game
- Purifiers barely work on a tank this size — purify a small bottle first, then pour it in
- Cannot be sold to anyone (value 0 and flagged not-for-resale) to keep the economy intact
- **Do not throw a full tank into the trash**: its cells go with it

## Languages

Chinese (Simplified), English, Russian. Other languages fall back to English.

## Settings

In the main menu under **Modding → Mod settings**, the `Diagnostics` section has one option:

| Setting | Default | Effect |
|---|---|---|
| Verbose log | Off | When on, writes init / patch-install / item-registration messages to `Player.log`; enable only when troubleshooting |

With it off the mod stays quiet during normal play. Warnings and errors are **always** logged regardless of this switch.

## Uninstall

Delete the `Mods\megatank` folder. Existing tanks stay in saves as empty shells; saves are not corrupted.
