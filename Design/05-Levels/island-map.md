# Island Map

Status: Draft
Last updated: 2026-10-13

## Summary

One large island map with distinct biomes, each designed to encourage a different style of dogfight. Airfields are spread around it as repair hubs.

## Regions

### Central City

- Skyscrapers and bridges
- High-risk, close-quarters dogfights: weaving between towers and flying under bridges
- Good for skilled players and Fighters

### Mountain Range

- Natural cover, valleys, and canyons
- Ambush territory: break line of sight and lose missile locks

### Ocean

- Aircraft carriers (also used in Carrier War)
- Wide open, long-range engagements
- Good for Interceptors and missile play

### Desert

- Open air combat with few obstacles
- Clean, pure dogfights

### Airfields

- Safe repair zones
- AA guns at home airfields
- Placed between regions so every region has a "nearest" airfield

## Layout Sketch (proposed)

```
        ┌───────────────────────────────┐
        │   ~~~~~~~ OCEAN ~~~~~~~        │
        │  ~  [Carrier B]            ~   │
        │ ~   ┌──────────────┐        ~  │
        │~    │ MOUNTAINS  ✈ │ DESERT  ~ │
        │~ ✈A │      CITY    │   ✈ B   ~ │
        │~    │  ✈ (neutral) │         ~ │
        │ ~   └──────────────┘        ~  │
        │  ~  [Carrier A]            ~   │
        └───────────────────────────────┘
  ✈A / ✈B = team home airfields   ✈ = neutral/capturable
```

## Design Rules

- **Retreat distance:** from any point of action, a friendly airfield should be roughly **10–20 seconds** of flight away (proposed). Matches are only ~5 minutes, so a long retreat takes too much of the match. Too close removes the risk.
- **Approach lanes:** each airfield should have 2–3 approach routes, so retreating players can't be trivially intercepted.
- **Landmarks:** each region needs a big landmark visible from far away for navigation.
- **Vertical space:** set a ceiling altitude. Clouds at mid-altitude give cover.
- **Boundary:** an ocean edge with a soft turn-back.

## Scale

Sized for **up to 16 players** and **~5-minute matches**. With 16 planes, a big map feels empty fast, so keep it compact. Starting point: the map should be crossable in **~45–60 seconds** at a Fighter's cruise speed (proposed). Players should find a fight within ~10 seconds of spawning.

Teams mode airfields (proposed): 2 home airfields + 1 central neutral, capturable airfield.

## Open Questions

- [x] Player count: up to 16
- [ ] One map at launch, or several smaller ones? (Recommended for solo scope: one map)
- [ ] Are city buildings destructible?
