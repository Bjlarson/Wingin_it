# Design Docs

Living design documentation for the project. Keep docs here, not in Unity's `Assets/` folder, so they stay out of the build and are easy to read in any editor.

**Start here:** [Game Overview](00-Overview/game-overview.md) · [Development Plan](development-plan.md) · [Open Questions](00-Overview/open-questions.md)

| Folder | What goes here |
|---|---|
| `00-Overview/` | High-level pitch, vision, pillars, target audience, scope |
| `01-Game-Design/` | Core loop, goals, progression, win/lose conditions, game flow |
| `02-Mechanics/` | One doc per mechanic: rules, numbers, edge cases |
| `03-Art/` | Art direction, style guide, palettes, references |
| `04-Audio/` | Music and SFX direction, mixing notes, audio cue list |
| `05-Levels/` | Level concepts, layouts, encounter and pacing notes |
| `06-UI-UX/` | Screen flows, HUD, menus, input mapping, accessibility |
| `07-Narrative/` | Story, characters, dialogue, world lore |
| `08-Technical/` | Architecture, systems, data formats, performance budgets |
| `Templates/` | Starter templates to copy when making a new doc |

## Conventions

- One topic per file, named in kebab-case (e.g. `combat-damage-model.md`).
- Start every doc with a status line: `Status: Draft | In Review | Approved`.
- Link related docs with relative paths.
