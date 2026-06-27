# Bookkeeper

A **read-only** storage cataloging mod for [Vintage Story](https://www.vintagestory.at/) (1.22, .NET 10).

Place a **Bookkeeper's Lectern** anywhere and interact with it to open a ledger that
scans every container within range and shows you, at a glance, everything you have
stored — across chests, trunks, barrels, tool racks, display cases, and shelves.

> **Read-only by design.** The ledger can browse, search, filter, and locate items.
> It **cannot** withdraw, deposit, move, merge, or delete anything. Nothing in your
> containers is ever modified, which makes it safe to use on multiplayer servers.

## Features

- **Aggregated catalog** of all storage within range, with item counts.
- **Search** by name and **filter** by category (Food, Tools, Fuel, Wood, Wearables,
  Ores & Metals, Building).
- **Locate** — middle-click an item to highlight the containers holding it with blue
  block markers, floating through-wall labels, and temporary map waypoints.
- Works **anywhere** — no foundation requirement.

## Usage

- **Open the ledger:** look at a Bookkeeper's Lectern and press `K`, or right-click it.
- **Locate an item:** middle-click it in the grid. Highlights clear as you open each
  located container.
- **Search/filter:** type in the search box or toggle the category buttons on the right.

## Crafting

Bookkeeper's Lectern (3×3 grid):

```
chisel  paper   charcoal
        planks
        planks
```

- `chisel` — any chisel (used as a tool; consumed as durability, not used up)
- `paper` — 1× parchment paper
- `charcoal` — 1× charcoal
- `planks` — 2× planks (any wood)

## Configuration

Server config at `ModConfig/BookkeeperConfig.json`:

| Setting | Default | Description |
|---|---|---|
| `ChunkRadius` | `2` | Horizontal scan radius in **chunks** (1 chunk = 32 blocks). |
| `VerticalRange` | `5` | Vertical range in blocks above/below the player. |

The scan is centered on the player and is chunk-granular; containers in chunks that
aren't currently loaded are not included.

## Building & deploying

Requires the Vintage Story DLLs. Set `VINTAGE_STORY_PATH` in `Bookkeeper.csproj` to your
install, then:

```bash
./deploy.sh
```

This builds the mod (net10.0) and packages `BookkeeperMod.zip` into your
`VintagestoryData/Mods` folder.
