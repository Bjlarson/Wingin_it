# Bots

Status: Draft
Last updated: 2026-10-16

## Summary

Bots fill empty team slots so a solo player gets a believable 8v8 (see [match-modes](../01-Game-Design/match-modes.md)), and so the game can be tested without 16 people. They fly **the same planes, through the same controls** as players: no cheating physics, no special damage rules.

## Rule: bots are just another input source

A bot is a `BotPilot` component that writes one `FlightInput` per physics step into the plane's `PlaneController.SetInput`, exactly like `PlayerFlightInput` does. Consequences:

- Lost parts, body damage states, the dead-engine glide, the boundary turn-back and landing rules all hit bots the same way as players.
- Weapons fire through the same input flags, so ammo, overheat, lock-on and jams apply.
- Later the dedicated server can run bots as one more input stream (see [multiplayer](../08-Technical/multiplayer.md)).

It's split in two layers:

| Layer | Job | Status |
|---|---|---|
| **Pilot** (`BotPilot`) | Fly an order and hold buttons: stick, throttle, triggers | Built (Phase 5 step 1) |
| **Behaviors** (`BotBrain`) | Pick the order each moment: patrol, chase, attack, evade missiles, avoid terrain, retreat to repair | Built (Phase 5 steps 2–3) |
| **Skill** (`BotDifficulty` → `BotSkill`) | How well it does all of that: Easy / Normal / Hard | Built (Phase 5 step 4) |
| **Roster** (`BotRoster`) | Fill every team's empty slots with bots, pick their classes and patrols | Built (Phase 5 step 5) |

## Pilot orders (built 2026-10-16)

| Order | What the bot flies |
|---|---|
| **Cruise** (default) | Straight and level on its heading, at cruise speed or an ordered speed |
| **FlyTo** (point) | Heads for the point, including its altitude. Within 150 m it counts as arrived and **orbits** it |
| **Orbit** (point) | Circles the point (300 m radius) at its altitude, turning whichever way needs no reversal |
| **Pursue** (plane) | Points at the gun lead point when the target is in gun range (tighter tracking), otherwise at an intercept point (capped at 3 s of lead). Full throttle beyond 600 m, then the target's speed + 8 m/s. Target dead or gone → Cruise |
| **FormUp** (plane, offset) | Holds a slot in the leader's level frame (e.g. 60 m behind, 60 m left), aiming 150 m ahead of the slot and trimming speed by how far behind it is |
| **Land** (airfield) | Tracks the runway's glide path from wherever it is (see [Retreat to repair](#retreat-to-repair-built-2026-10-16)), at approach speed. On the wheels it idles, so the auto-brake parks it on the repair zone's cross |
| **TakeOff** | Throttle to cruise, pull up at flying speed, then a steady 12° climb straight ahead until the next order |

Buttons (boost, guns, rockets, missile, bombs, ability) are held until the behavior layer changes them, and all let go when the plane dies.

**How it flies:**
- Steering uses the same `FlightAutopilot.SteerTowards` as mouse-aim, so a bot turns like a mouse-aim player.
- The stick is **slew-limited** (full deflection in 0.2 s at Normal), so bots don't snap between extremes. This and the stick gain are set by the [difficulty](#difficulty-built-2026-10-16).
- Tracking the lead point (inside the wings-level cone) adds an **integral term** to the stick, so it settles on the steady deflection a turning target needs instead of trailing it by a couple of degrees (a plain proportional stick does). Without it, a Normal bot couldn't finish an orbiting drone in a minute.
- In gun range it closes to **~300 m**, just inside the guns' 350 m convergence: up to 25 m/s faster than the target far out, matching speed at 300 m, dropping back inside it.
- In gun range its aim **wanders** off the lead point by up to the difficulty's aim error, slowly (a couple of seconds per swing), so a bot drifts on and off the target like a person rather than holding the pipper dead on.
- The throttle is a real throttle input (like holding W/S) aimed at the setting that flies the target speed. It accounts for lost thrust and works on the runway too.

## Behaviors (built 2026-10-16)

`BotBrain` sits on top of the pilot. Every physics step it picks one state, highest priority first:

| Priority | State | When | What it does |
|---|---|---|---|
| 1 | **AvoidTerrain** | Something solid in the next 3.5 s of flight (a 12 m sphere swept along the velocity), or under 50 m above the ground and not climbing, or sinking fast enough to get there | Takes the stick: climbs at ~40° and turns away along the obstacle's surface normal (a wall turns it aside, flat ground just pulls the nose up). Keeps pulling for 1 s after it's clear |
| 2 | **AvoidCollision** | Another plane (friend or foe) will pass within 35 m in the next 2 s, both flying straight | Steers ahead and away from where it will pass. Dead-on, it breaks right, so two bots head-on pass left side to left side |
| 3 | **Evade** | A missile chasing it is within 550 m (after a 0.2 s reaction delay; both set by difficulty), if it noticed that missile at all (65% at Normal, rolled once per missile) | Boosts and holds a maximum-rate turn (steering 100° off the nose) into its current bank, or away from the missile if wings are level. That's the hard turn that shakes missiles. Inside 250 m it uses its ability if that helps (see [Abilities](#abilities)) |
| 4 | **Retreat / Land** | Damaged or out of ammo (see [Retreat to repair](#retreat-to-repair-built-2026-10-16)) | Flies an approach lane home, then the final approach. Fires its guns at anything that crosses its nose, but doesn't turn to fight |
| 5 | **Attack** | A target is in range of any weapon it carries | Pursues it. Guns fire when the lead point is within 3° of the nose **and** the bullets would pass within 7 m of it, inside 550 m (the cone narrows with range; long shots straddle the body with wing guns), letting go at 85% heat until they cool to 45% (never overheats). Rockets fire within 2° and 70% of range. Missiles fire once the lock has been on its target for 0.4 s. (Angles, distances and delay at Normal) |
| 6 | **Chase** | A target, but out of range | Pursues it, boosting beyond 1 km while there's boost to spare |
| 7 | **Patrol** | No target | Flies its patrol route in a loop, or circles where it started if it has none |

**What a bot knows** is what a player could: enemies within 1.8 km (at Normal), unless 40 m+ of cloud hides them (an allied Support's radar still reveals them), and the missile warning. It re-picks its target every second and only switches for a clearly better one (30% better score). The score is the distance, stretched up to 2× for a target behind it, then weighted by **how easy the kill is**: a target showing its tail scores down to 0.7× (it isn't turning to meet us), head-on up to 1.3×, and a damaged one down to 0.6× at no body HP. **Stalemate breaker:** in gun range for 6 s without the lead point coming within 10° of the nose (two equal planes in a turning circle can't out-turn each other), it takes any better target. It drops a target beyond 2.4 km, or after 3 s hidden. A **first sighting** takes a reaction time (0.5 s at Normal) before it turns to fight; switching targets mid-fight doesn't.

Reflexes (terrain, other planes, missiles) use `BotPilot.Override`: they take the stick for one step at a time and the order keeps running underneath, so the bot goes straight back to what it was doing.

On the ground there are two more states: **Repair** (parked in the zone while the timer runs) and **TakeOff** (after a repair, a runway spawn, or a missed zone), which keeps climbing straight out until 60 m up before anything else gets a say.

### Abilities

Every class uses its ability (except at Easy):

| Ability | When |
|---|---|
| **Barrel Roll** (Fighter) | Evading a missile that's inside 250 m |
| **Afterburner** (Interceptor) | Evading a missile inside 250 m; chasing a target over 1 km away; retreating with an enemy within 800 m |
| **Armor Shield** (Heavy) | Hit in the last 0.5 s with an enemy in sight; evading a missile inside 250 m |
| **Repair Aura** (Support) | It, or an ally inside the aura (80 m), has its body under 75% |

## Difficulty (built 2026-10-16)

`BotBrain.Difficulty` picks a preset (`BotSkill.For`): **Easy**, **Normal** (the default, and the tuning everything above was built at) or **Hard**. Setting it takes effect at once, and `ApplySkill` takes custom values for tuning. The plane and its physics never change: a Hard bot flies the same plane, it just flies it better.

| Knob | Easy | Normal | Hard |
|---|---|---|---|
| Notices enemies within | 1.3 km | 1.8 km | 2.2 km |
| Reaction to a first sighting | 1.2 s | 0.5 s | 0.2 s |
| Looks for a better target every | 2 s | 1 s | 0.5 s |
| Aim wander off the lead point | ±3.5° | ±1.2° | ±0.4° |
| Guns fire within (wider = more spraying) | 6° | 3° | 2.5° |
| ...and with bullets passing within | 14 m | 7 m | 4 m |
| ...inside | 800 m | 550 m | 450 m |
| Rockets fire within | 4° | 2° | 1.5° |
| Holds a missile lock before firing | 1.2 s | 0.4 s | 0 s |
| Notices a missile chasing it | 45% | 65% | 85% |
| Breaks from missiles at | 400 m | 550 m | 700 m |
| Missile reaction delay | 0.6 s | 0.2 s | 0.1 s |
| Uses its ability | No | Yes | Yes |
| Goes home below body | 25% | 40% | 50% |
| Stick gain | 2.2 | 3 | 3.6 |
| Stick slew (full deflection) | 0.4 s | 0.2 s | 0.125 s |

**Not knobs:** terrain and collision avoidance are the same for every bot. An Easy bot flying into a hill isn't a fair kill for anyone, and it would look broken rather than beginner. Losing a part sends every bot home.

Easy bots spot late, turn late, spray, stay in a losing fight and are slow to break from missiles, so they die a lot and feed kills, but they still shoot back. Hard bots go home before they're in real trouble (at half health), which makes them harder to finish.

**Who picks it:** the match: `BotRoster.Difficulty` sets every bot at once (Normal by default; changing it in the Inspector during play applies straight away). The settings menu (Phase 6) will let a solo player choose it. Online, the server picks (⚠ open below).

## Filling team slots (built 2026-10-16)

A `BotRoster` in the level keeps every team at **8 planes** (`TeamSize`), humans and bots together. Any plane without a `BotBrain` counts as a human. On the Island the player is on team 0, so it starts **the player + 7 bots vs 8 bots**.

**Class mix:** each new bot flies the class its team is furthest below its share of, counting the humans too. The target mix is **3 Fighters, 2 Interceptors, 2 Heavies, 1 Support** (weights 3/2/2/1). Ties go to the bigger share, so the first four bots are one of each class: even a small team gets every role. A team of human Heavies gets no Heavy bots.

**Where they appear:** over their home airfield, exactly like a respawn (`PlaneRespawner.SpawnNow`), so they follow the level's spawn mode (in the air, or parked on the runway). Air spawns now have **9 slots** like the runway: the air spawn, 40 m either side of it, then rows 60 m further back and 10 m higher. A plane appearing takes the first slot with nobody within 25 m, so a team spawning (or respawning) together doesn't appear inside itself. Players use the same slots.

**Where they go:** the level lists its **fight areas**: on the Island, the city, Summit Radar, the Pyramid, Central airfield and Lighthouse Rock, each 150–250 m above whatever's under it (mirrored across the island, so both teams' areas are the same distances from home). Each bot patrols between two of them, sorted nearest-home first: the first five take neighboring pairs (home landmark ↔ city, city ↔ Central, …, enemy landmark ↔ home landmark), the next ones skip one. Every area gets at least two bots per team, and both teams keep crossing the middle, so they meet. Patrol is only what a bot does with nothing in sight: it fights whatever it spots on the way.

**Does it play like a match?** The exit check (`BotRosterTests.EightVersusEight_BotsMeetAndFight`) flies a full 8v8 of Normal bots for 3 minutes. Before the tuning above it scored **no kills at all**: bots spent most of the fight in turning circles, fired from out past the convergence, trailed their target by ~2°, and dodged every missile. After it, a run scores ~6–8 kills in 3 minutes, split between the teams, with 15–30 parts shot off, many retreats, a few repairs, and at most a few crashes nobody caused. Many kills are crippled planes going down on the way home within 20 s of the last hit, which the game credits to the shooter.

**Players joining and leaving:** the roster checks every 2 s. A team short of planes gets a bot at once. A team with too many loses a bot the **next time one dies**, never one that's mid-fight. (Online this is the server's backfill, Phase 7.)

**Tuning lives on the roster:** team size, class weights, difficulty, the fight areas and the check interval. `Clear()` removes every bot (tests that need an empty sky use it).

## Retreat to repair (built 2026-10-16)

**When:** any part lost (a wing, a stabilizer or the engine), or the body under **40%**. Also when a class that carries missiles or rockets has none left and there's no enemy around (it goes home to restock). Once it's on final it commits; before that, being healed back above the line (a Support's Repair Aura) calls the retreat off. No friendly airfield: it keeps fighting.

**Where:** the nearest friendly airfield, down one of its [approach lanes](../05-Levels/island-map.md#approach-lanes-built-2026-10-16):
- Each lane's cost is the trip: across to the lane, then the rest of it. Any lane within **1.3×** of the cheapest is picked at random, so a chaser can't always guess the way home.
- It joins at the first lane point past its closest spot on the lane (the entry, if it's before the lane). It never joins at the final approach fix itself: close to the end, it goes back for the point before it, so it always arrives lined up.
- It slows to approach speed with **900 m** of lane left, and boosts before that if an enemy is within 800 m.
- A field without lanes (FlightTest; Central later) gets a straight-in: a point 1,100 m out on the glide path, 40 m above it, then a fix 600 m out.

**Landing:**
- Within **350 m** of the fix it turns onto final and flies the **Land** order: it tracks a point on the glide path ahead of it (250 m ahead, closing to 100 m near the runway, so it turns in harder when still off the centerline). Past the aim point that line carries on below the runway, so the descent never flattens out.
- It puts its **flight path**, not its nose, on that point: a plane sinking from lost lift holds its nose up to stay on the path, like a player flying the flight-path marker.
- Approach speed is **1.2× the class min speed**, rising to **~1.5×** with a wing gone (only speed beats the extra sink). Both are under the 1.6× bounce limit.
- It banks to line up while there's room, then keeps the wings level for the last 200 m. Banked past **35°** (a lost wing's roll can run away with it), it levels the wings before anything else.
- Terrain avoidance changes on the way down: the minimum height drops to **20 m** on the last lane leg and while turning onto final. Once lined up, only things sticking up above the glide path count (the runway, its edge and its pad don't). Sinking **6 m** under the path brings the full check back.

**Go-around:** it abandons the landing and re-plans the route from where it is when:
- the runway is taken while it's still 300 m+ out;
- it's off the approach for **4 s** (overshot the turn, or pulled off by a missile break);
- it would touch down **250 m** past the aim point;
- it's about to touch down beside the runway.

If the runway is taken before final, it circles the point before the fix until it clears.

**On the ground:** it idles and lets the auto-brake stop it on the cross, which starts the repair. Stopped for **2 s** without a repair (it landed short or long of the zone) means it missed: it takes off and flies the approach again. When the repair finishes it boosts down the runway, takes off, and goes back to whatever it was doing.

## Tested

- EditMode (`BotSteeringTests`): throttle commands (air, ground, lost thrust, dead engine), intercept lead + cap, formation slot frame + speed trim, orbit point, stick slew.
- EditMode (`BotSensesTests`): target scoring, aim cone, gun heat discipline, break side and break-turn point, too-low check, escape points from the ground and walls, collision course and sidestep (head-on bots separate).
- PlayMode (`BotBrainTests`, FlightTest scene): patrols a two-point route in a loop; circles home with no route; spots an enemy, chases it and lands hits; ignores allies; breaks from a missile fired up its tail and shakes it off; pulls out of a dive at the deck; flies low through downtown without hitting a tower; two allies head-on sidestep instead of colliding; two bots dogfight for 90 s, land hits and neither flies into anything.
- EditMode (retreat): repair decision, lane join point and trip cost, the random-but-fair lane pick, go-around reasons, the glide-path aim point, flight-path aim and split aim.
- PlayMode (`BotRetreatTests`, FlightTest scene): a bot at 30% body flies home, lands first time, repairs (Heavy), takes off and resumes its patrol; **a one-winged bot lands and gets its wing back**; 70% body keeps flying; healed in the air calls it off; a runway spawn takes off; a runway taken by a parked plane makes it hold, then land once it clears; no friendly field, no retreat.
- PlayMode (`IslandBotRetreatTests`): a bot flies **each of the six Island lanes** home and stops in the repair zone with no go-around.
- EditMode (`BotDifficultyTests`): every knob gets harder from Easy to Hard; Normal keeps the tuning the behaviors were built at; every level spots inside the disengage range; Easy still has time to break from a missile; the aim offset and wander math.
- PlayMode (`BotBrainTests`, difficulty): setting a difficulty sets the skill and the pilot's stick; Hard notices an enemy 1.7 km away and Easy doesn't; Easy notices one but waits out its reaction time before chasing; an Interceptor lights the afterburner on a long chase; Easy never uses its ability; a Heavy raises its shield when hit; a Support switches on its aura when hurt; **Hard outfights Easy** in two 90 s fights with the classes swapped.
- EditMode (`TeamFillTests`): an empty team fills to the class mix; every partial team is as close to the mix as it can be; humans count toward it; a zero-weight class is never picked; patrol pairs cover every area and never repeat within a team (and cope with few areas); how many bots a team wants.
- EditMode (combat tuning): tail-on and damaged targets score better; the gun trigger cone narrows with range and stops at max range; pursuit overtake closes fast, matches at the gap, backs off inside it.
- PlayMode (`BotRosterTests`, Island): both teams at eight with the player in one; the class mix; bots start over their own field clear of each other; patrols spread across the fight areas; fight areas clear of terrain and the home AA; difficulty reaches every bot; a human joining takes a bot's slot when it dies; a bot that leaves is replaced; **8v8 for 3 minutes: both teams score kills, crashes nobody caused stay rare.**
- PlayMode (`BotBrainTests`, combat): a Normal bot **shoots down the orbiting Heavy drone** inside a minute; a missile the bot didn't notice hits it.
- PlayMode (`BotPilotTests`, FlightTest scene): cruise holds heading, altitude and the ordered speed; FlyTo reaches a point behind and below, then orbits it; **a one-winged bot still gets there**; Pursue closes on the orbiting Heavy drone and gets a gun solution; triggers fire the guns through the input; Pursue falls back to Cruise when the target dies; FormUp holds a slot within 80 m on the circling Support drone; a crash releases the buttons.

## Open

- The FlightTest target drones (`DummyPilot`) still fly their own patterns. Move them onto `BotPilot` once behaviors exist.
- Difficulty online: one level for the whole match, or matched to the players' skill? Should a team's bots get harder to make up for a missing player? Decide with matchmaking (Phase 7).
- The difficulty numbers are a first guess. **Needs a hands-on session** against each level: is Easy beatable by a new player, and is Hard a challenge without feeling like aimbots?
- A Support bot doesn't fly toward hurt allies to heal them; it only switches the aura on when one happens to be close.
- Bombs aren't used: there are no ground objectives until Phase 8.
- Bots don't avoid flying through allies' line of fire yet.
- A bot that climbed over an obstacle can be slow to settle onto a low waypoint (it circles down to it).
- Two bots on final into the same runway: the second only notices the first once it's on the wheels (then it goes around or holds). Spacing on final isn't planned.
- A dead-engine bot glides for the nearest field like any other retreat, but nothing checks it can make it, so it may land short or crash. A real "can I reach it?" check would help.
- A bot that lands short or long of the zone takes off and goes around instead of taxiing to it.
- The straight-in default (no lanes) can make a bot overfly the field and U-turn at low level. Only FlightTest uses it today.
