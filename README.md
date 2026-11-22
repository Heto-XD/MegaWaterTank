# Mega Water Tank / 巨型水箱

A water-storage mod for **Probably Stolen** (Unity / Mono, official built-in ModLoader).
一个给《Probably Stolen》用的储水 mod（官方内置 ModLoader）。

- **Main tank + 16 insert cells** — 0.1 t (100 L) to start, 1.9 t at max, **32.3 t** fully loaded
- **Overflow / refill** — extra water automatically flows into the cells; the main tank pulls it back when it drops below 90%
- **Real water** — hydroponics, purifiers and the water scanner recognise it through the vanilla rules
- **Vanilla-style item data** — container value 350 (tank) / 25 (cell), metal/plastic sounds, and it fits into any container the vanilla tank fits in
- **Vanilla tooltip** — volume, quality and scanner results come from the vanilla water-container tooltip; the mod only appends a "Total" and a "Cells" line
- **Water tint** — the water colour follows the liquid inside, using the same computation as vanilla bottles
- **Phone ordering** — the "Hetao Water Co." contact (#21474836#) opens the service page; capacity expands +100 L per purchase (18 steps), no cooldown
- **Localisation** — Chinese / English / Russian

---

## Install

1. Make sure the game's own mod loader is active (the game ships with it; no MelonLoader needed).
2. Copy the `megatank` folder into `Probably Stolen\Mods\`:

```
Mods\megatank\
    MegaTank.dll
    manifest.xml
    Localization\
    Textures\
```

3. Start the game. The main menu **Modding** panel should list *巨型水箱 / Mega Water Tank*.

Uninstalling is safe: tanks left in a save become inert placeholder items, and the phone
contact is removed automatically (the mod never stores it in the save).

## Compatibility

Tested together with Item Stacking, Container Manager, Item Details and Water Machine Fix.
Filled containers are frozen while stacked (same rule as vanilla liquid containers).
Item Details keeps its water analysis for ordinary bottles; this mod keeps its own tooltip
for its two items.

## Build from source

Requires the .NET SDK and a copy of the game's `Managed\` folder for assembly references
(see `MegaTank.csproj` for the paths).

```bash
dotnet build MegaTank.csproj -c Release
dotnet run --project tests/PureCheck/PureCheck.csproj   # 51 pure-logic assertions
```

> The optional symbol check compares game API usage against a local decompiled copy
> (`-p:DecompDir=<path>`); it is skipped automatically when that copy is absent.

## License

**MPL 2.0** — see [`LICENSE`](LICENSE). Attribution and third-party notices are in [`NOTICE`](NOTICE).

A large part of this project (code, artwork and text) was produced with **generative AI
assistance** and then reviewed and play-tested by a human. Scope, quality notes and the
copyright position are described in [`AI-DISCLOSURE.md`](AI-DISCLOSURE.md).

## Credits / disclaimer

- *Probably Stolen* and its assets belong to Questing Goose Studio. This is an unofficial
  community mod, not affiliated with or endorsed by the developer. No game assets are
  redistributed, and no third-party libraries are bundled (`0Harmony` / `Newtonsoft.Json`
  come with the game).
- Workshop page: <https://steamcommunity.com/sharedfiles/filedetails/?id=3842872840>
