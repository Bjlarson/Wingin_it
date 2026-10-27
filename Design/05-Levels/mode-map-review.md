# Modes on the Island: Review

Status: Draft
Last updated: 2026-10-26

## Summary

Phase 6.7's first step: how each of the four modes (Teams, Capture the Banner, Airfield Domination, Ram Royale) plays on the Island, which every mode shares today. What fits, what's cramped, what's missing. This review feeds the next step, **deciding the map list**. It decides nothing itself.

## How this was measured

- **Layout:** the real `Island.unity` (airfields, AA guns, banner stands, clouds, lanes, landmarks, the Ram Royale arena), read in the Editor. Times are at Fighter cruise (85.5 m/s).
- **Play:** `MapSurveyTests` (PlayMode, `[Explicit]`, run by hand) plays a full match of each mode on the Island with **bots only** (the player flies as a bot too), at 4× speed, Normal difficulty. Every 0.5 s it samples each live plane: which region it's in, its height, how near the closest enemy is, and the bot's state. It also logs every death (where, how, who), banner and field events, and planes past the edge. The output goes to `Logs/MapSurvey-<mode>.txt`.
- **Runs:** three survey runs per mode on 2026-10-26. The first run's region labels were too coarse, so the numbers below come from runs 2 and 3, and the ranges give both runs. "In a fight" means an enemy within 600 m; "lonely" means no enemy within 1.5 km.
- **Caveat:** these are bots, not people, and only a few matches. The bots follow the patrol areas and mode duties they were given, so a region nobody goes to may be a bot choice as much as a map problem. Region shares change a lot from run to run (Teams' city share was 22% in one run and 54% in the next). Treat these as signs to check in the playtest, not verdicts.

| | Teams | Banner | Domination | Ram Royale |
|---|---|---|---|---|
| Time in a fight | 85% | 75–81% | 68–73% | 100% |
| Lonely | 5–7% | 7–9% | 9–12% | 0% |
| First contact (900 m) | 12.5 s | 13.5 s | 13.0 s | at once |
| Mean height above ground | 330–350 m | 260–340 m | 170–200 m | 205–220 m |
| Under 150 m | 9–12% | 12–14% | 41–45% | 19–28% |
| Bot time spent avoiding (planes + terrain + edge) | 24% | 22–24% | 16–17% | 52–56% |
| Deaths per 5:00 | 15–21 | 11–18 | 17 (full match) | 7–11 of 16 |

## Teams

**Fits.** This is the mode the Island was built for, and it shows. First contact comes at 12.5 s (the target is about 10 s), 85% of plane time is spent near an enemy, and only 5–7% is lonely. Retreats, lanes, clouds and the boundary all work as designed (0% of plane time past the edge in the last run).

**Cramped.** The fight **funnels into the city center**. The city held 22–54% of all plane time and the fight heat map peaks in the two or three 400 m cells around downtown. The regions built for different kinds of fight barely get used: mountains 2–9%, desert 11–13%, the south sea round Lighthouse Rock under 1% (the coastal sea, at 14–21%, is mostly the fringe of the city fight). Bots also spend **14–16% of their time dodging each other** (other modes: 5–9%), which is 16 planes in one knot.

**Missing.** A reason to go anywhere else. Central is inert in Teams until Phase 8 (airfield capture), and so are the convoys, radar stations and supply drops that would pull fights to the mountains, desert and coast. Part of this is also the bots' patrol areas.

**Verdict:** the Island fits Teams. Its spread problem is what Phase 8's objectives are for, not a reason to build a new map.

## Capture the Banner

**Bots don't play it yet.** In two full matches **nobody grabbed a banner**. The runners flew right over the enemy stand (5–27 m from the pole) but never below the 40 m grab height near it; their closest low pass was 369 m away or more. So the mode can't be judged from bots until the runners' grab run is fixed. That is a bot bug, not a map problem (see Follow-ups).

**Map problems that hold anyway:**

- **The stands are under the full home flak.** Each stand is 450 m past its runway's end and only 241 m from the nearest home AA gun, so **all four** home guns (800 m reach) cover the grab zone. The design said a stand should sit far enough out that the grab run isn't through all four guns.
- **The fight sits at the stands.** About 50% of plane time, and as much of the fighting, is within 400 m of a home field or a stand, inside AA. That's the attackers circling a stand they can't get low on, and the defenders' guard orbit.
- **A capture run is long.** The stands are 3.3 km apart: 39 s at cruise, **about 52 s carrying** at the banner's −25% top speed, revealed to every enemy the whole way. Three captures in 5 minutes (the early win) needs three of those runs to get through. Expect 0–1 captures a match, and matches decided by team score.
- **No cover near the stands.** The nearest cloud is 825 m from each stand. Only West's Canyon Run gives terrain cover on the way out, and East has no canyon.

**Wants:** stands closer together (about 2 km, so 30 s or less carrying), off the home AA umbrella or under only one or two guns, and cover along the carry routes on both sides.

## Airfield Domination

**Fits best of the three new modes.** The fight spreads over the three fields (fight share: Central 21–29%, South East 21–30%, South West 12–13%). It's the only mode where planes fly low (41–45% of the time under 150 m) and land a lot, as the design wanted. A full match flips fields often (9 captures in one run).

**Cramped / unfair:**

- **Everything happens in the south third.** All three capture fields are south of the city (z = −700 to −900). The mountains got **0%** of plane time, and Summit Radar, the Pyramid and the north coast are just scenery in this mode.
- **The fields are close.** Central is only 9.1 s from each south field, and their light AA umbrellas overlap (Central's west gun and South West's north gun are 420 m apart, each with 600 m reach). Holding Central plus your near field makes one flak zone.
- **Early wins come fast.** In all three runs **West took all three fields first**, and two runs ended on the hold-every-field win, after 108 s and 199 s of a 5:00 match. With the fields 9 s apart, holding all three for 30 s is easy once a team has momentum. Why it's always West is not known yet: West's lanes or terrain, the bots, or the player-as-bot slot. It needs a look (see Follow-ups).
- **The two south fields aren't mirror images.** The ground within 1 km of South West rises to 358 m (the mountain foothills); around South East it never passes 20 m (the desert). Both land north off the south sea, but South West has high ground for cover and in the way, and South East has none.

**Wants:** fields spread across the whole map (one per region, or a broader map), far enough apart that holding them all is a stretch, with matching terrain on each team's side.

## Ram Royale

**Cramped.** The arena is 700 m round Lighthouse Rock: about **0.1 km² per plane** for 16, against 1.3 km² on the full Island, and 16 s to cross. Planes are always near each other (100% "in a fight"), but bots spend **over half their time not ramming**: 28–30% turning back from the hard edge and 22–27% pulling up from the sea.

**Slow eliminations.** The survey ran a 5:00 clock (the test starts the mode outside its card, so it didn't use the card's 3:00), and only 7–11 of 16 planes went out. Several of those crashed into the sea after a ram, not from the ram itself. At 3:00 most matches would end on the clock and be decided by body HP, not last plane flying.

**Missing.** Things to fly around. The arena is 98% open sea under a 450 m ceiling. The lighthouse is the only obstacle, and the sea is a floor the bots keep pulling up from.

**Wants:** a purpose-built arena. More room per plane (a bigger circle, or fewer than 16), obstacles to weave and trap with (sea stacks, arches, a canyon or bowl), and a shape that keeps planes off the edge (round walls or terrain, not an invisible line). The shrink and 16-vs-fewer questions are still open in [match-modes](../01-Game-Design/match-modes.md#open-questions).

## Across all modes

- **Planes far past the edge:** in the second survey run, Domination planes spent 2.7% of their time past the 2.6 km boundary, one of them **17 km out**. The first run logged crashes 6–9 km out in Teams and Banner. The third run, which logs each plane that gets 500 m past the edge, saw none (furthest 2.7 km). So it's real but rare. The soft turn-back should make it impossible.
- **The north half of the Island** (mountains, Summit Radar, the Pyramid, the north coast) gets little use in any mode: 0–9% of plane time.
- **Teardown noise:** one Domination run ended with Unity's "Some objects were not cleaned up when closing the scene: VFX" error. That's the test harness, not the map.

## What this suggests for the map list

For the next step ([development plan](../development-plan.md#phase-67--additional-maps-planned-note-added-2026-10-20)), proposed only:

| Mode | Island as it is? | Suggestion |
|---|---|---|
| Teams | **Yes** | Keep the Island as the Teams map; Phase 8's objectives spread the fight |
| Airfield Domination | Workable, lopsided | Either move the capture fields on the Island to use the north (one per region) or build a broad open map for it |
| Capture the Banner | Can't tell yet | Fix the runners first. Then either move the Island's stands (closer, out of the full AA) or build a smaller symmetric map |
| Ram Royale | **No** | Its own small arena map, with obstacles and room |

## Follow-ups

- [ ] **Banner runners never grab:** they overfly the enemy stand but never go below 40 m near it (bot fix; blocks judging Banner)
- [ ] **Domination: West took all three fields first in 3 of 3 runs**: find out whether the map, the bots or the test favours West
- [ ] **Planes 6–17 km past the boundary**, seen in 2 of 3 survey runs: trace it with the survey's stray log
- [ ] Ram Royale: the survey ran 5:00; rerun it on the card's 3:00 clock for the elimination count

## References

- `Assets/_Project/Tests/PlayMode/MapSurveyTests.cs`: the survey. Run with `run_tests --mode playmode --include_explicit true --filter WingingIt.Tests.MapSurveyTests` (~15 min for all four).
- [island-map](island-map.md) · [match-modes](../01-Game-Design/match-modes.md) · [bots](../02-Mechanics/bots.md)
