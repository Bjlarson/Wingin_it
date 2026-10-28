# Match Modes

Status: Draft
Last updated: 2026-10-20

## Summary

Four core modes use the primary loop (fight + repair). Every mode awards player XP. One party mode uses the secondary "ram" loop. A racing mode ([Sky Racing](#sky-racing-prototype), proposed 2026-10-20) is a third loop.

**Mode prototypes (added 2026-10-19):** before networking, three more modes get built offline against bots to see how they feel: [Capture the Banner](#capture-the-banner-prototype), [Airfield Domination](#airfield-domination-prototype) and [Ram Royale](#ram-royale-prototype). Each is a quick prototype. A playtest then decides which ones ship (see [development plan, Phase 6.5](../development-plan.md#phase-65--mode-prototypes-offline)). Everything in those sections is **proposed** unless it says decided.

## Free For All

Every pilot for themselves.

- **Win condition:** TBD (highest score at time limit? First to X kills?)
- **Airfields:** neutral; anyone can repair. Need to define what stops spawn-camping at repair zones.

## Teams (Blue vs Red) — FIRST PLAYABLE

Team deathmatch with objectives. **This mode gets built first (decided).**

- **Size:** up to 8v8, with bots filling empty slots (proposed)
- **Length:** ~5 minutes
- **Win condition:** the highest team score when the timer ends. Kills and objectives both give score. **A tie is a draw (decided 2026-10-17; revisit after playtests).**
- **Airfields:** each team has a home airfield. Neutral airfields can be captured.

### Match Flow (offline prototype, built 2026-10-17)

`MatchManager` + `MatchScreens` (IMGUI placeholder screens) on the Island:

1. **Menu** (replaced the lobby, 2026-10-17): the main menu over the player's plane parked in its home hangar, then the game mode cards with the Teams card's bot difficulty (Easy / Normal / Hard). See [front-end-menus](../06-UI-UX/front-end-menus.md). The world is frozen behind it and no bots exist yet.
2. **Class select:** four cards (speed, turn rates, body HP, weapons, ability) and both rosters (You + 7 bots for West, 8 bots for East, with the classes the bots will fly); click **Fly**, press 1–4, or Enter for the highlighted one. Esc goes back to the game mode cards. The player spawns fresh over home in that class and the bots join.
3. **Countdown:** 3-2-1 in real time with everyone frozen in place.
4. **Play:** a 5:00 game-time clock with both team scores at the top center (it flashes in the last 30 s). Controls, the flight HUD and touch controls only run in this phase. Esc / Start / ☰ opens the in-match menu, which never pauses.
5. **End:** time's up freezes the world. VICTORY / DEFEAT / DRAW, the final score, and each team's pilots with kills, assists, deaths and score (sorted by score). **Play Again** (Enter) reloads the level straight to class select, keeping the difficulty and class; **Main Menu** (Esc) reloads it to the menu.

### Class Changes in a Match (built 2026-10-17)

Class select happens before the match, then again on every respawn (pick during the respawn timer) or while parked and repaired at a home airfield (Tab / CHANGE PLANE). Details in [aircraft-classes](../02-Mechanics/aircraft-classes.md#class-selection).

### Team Score (built 2026-10-17)

Every point a pilot earns goes to their team's score and their own (personal score, later the basis for coins and XP). Highest team score at the whistle wins; a tie is a draw.

| Event | Points | Notes |
|---|---|---|
| Kill | **50** | An enemy plane brought down by a pilot, or by the team's airfield AA (team points only, no pilot credit). Crash kill credit as in [health-and-damage](../02-Mechanics/health-and-damage.md#destruction) |
| Assist | **20** | Per other enemy pilot who helped: shot off a part this life, **or** did ≥ 20% of the victim's body max HP in damage (any parts) with a hit in the last 20 s. An AA kill still pays assists |
| Objective | Set by each objective | `MatchManager.AwardObjective(team, points, label, pilot)`: for Phase 8 convoys, supply drops, radar and airfield captures |
| Crash nobody caused, team kill | 0 | The death still counts on the scoreboard |

Assists count toward team score (so focus fire pays a little more than a duel: a kill with two helpers is worth 90). **Revisit after playtests** along with the point values. Every award raises `MatchScore.Awarded` (team, pilot, points, kind, label) for the score popups and kill feed still to come.

## Shared Mode Framework (prototype)

Today `MatchManager` hard-codes Teams. The three prototypes need it split up:

- **Mode rules object:** each mode supplies its win check, scoring, spawn rules, lives, loadout limits, HUD widgets and scoreboard columns. `MatchManager` keeps the phases (menu → class select → countdown → play → end) and asks the mode. Teams becomes the first mode written this way, with no change in behavior.
- **Game mode cards:** each new mode gets a card in the game mode scroll (`GameModeInfo`), with its own bot difficulty row.
- **Loadout limits:** a mode can switch weapons off (Ram Royale) or slow a plane down (the banner) through the existing modifier stack and the weapons' `IsEquipped`.
- **Free-for-all teams:** every pilot on their own team (Ram Royale; later Free For All).
- **Lives:** unlimited (default) or one life, with spectating after death.
- **Arena:** a mode can pick its own play area and spawns on the Island (a smaller boundary, extra airfields), instead of a new scene for each mode. Built: see [Arenas](#arenas-built-2026-10-19).
- **Bots:** each mode gives `BotBrain` its own goals (grab the banner, capture a field, ram a target). The flying and dodging underneath stay the same.

### Framework (built 2026-10-19)

- `MatchMode` (`Scripts/Match/MatchMode.cs`) is the rules object; each `GameModeInfo` card makes a fresh one per match. `MatchManager` runs the phases and asks it the rest.
- **Scoring:** `RecordKill(score, KillRecord)`, plus `Tick` every frame of play for timed points (fields held, a carried banner).
- **Winning:** `EarlyWinner` ends the match at once (3 captures, every field held, last plane); `Winner` decides at the whistle; `WinRule` is the scoreboard's footer line.
- **Lives and spawns:** `CanRespawn` (asked when the respawn timer runs out), `TryGetSpawnPose` (an air ring, an owned field), through `PlaneRespawner.Rules`.
- **Loadout limits:** `OnSpawned(plane)` after every spawn, where a mode switches weapons off or adds a modifier.
- **HUD and scoreboard:** `Widgets` (team scores, kill feed, score popups) and `Columns` / `CompareForScoreboard`.
- **Teams** (`TeamsMode`): kill 50, assist 20, unlimited lives, usual spawns, every widget, K / A / D / SCORE.

### Game mode cards (built 2026-10-19)

- `GameModeInfo.All` has four cards: **Teams**, **Capture the Banner**, **Airfield Domination**, **Ram Royale** (8 v 8 / 5:00, 8 v 8 / 5:00, 8 v 8 / 5:00, 16 FFA / 3:00), each with its pitch and a drawn top-down sketch (`ModeSketch`).
- **Bot difficulty per mode:** every card has its own BOTS row, remembered between sessions under `WingingIt.<Id>.BotDifficulty` (Teams keeps the old key, so saved picks survive). `MatchManager.DifficultyFor(card)` / `SetDifficulty(card, level)`; `MatchManager.Difficulty` is the played mode's and drives the bots.
- **In development:** a card with no `Rules` yet is not playable: it shows only in the Editor and development builds, with **IN DEVELOPMENT** in place of PLAY, and `SelectMode` refuses it. Release builds hide it, so players never see a locked card. A mode turns playable by setting its `Rules`.

### Arenas (built 2026-10-19)

A mode plays on the Island **as built** unless the level has an arena for it. The geometry lives in the level, not the mode: a `ModeArena` (spawned from the map's layout when the mode is picked, see [island-map](../05-Levels/island-map.md#mode-pieces-built-2026-10-26)) names the mode it's for (`GameModeInfo.Id`; `MatchMode.ArenaId` can point elsewhere), and the match puts it in force when the class is picked (before anyone spawns) and puts the Island back on Dismiss or when the scene goes.

- **Play area:** center, radius, ceiling and edge type. The `FlightBoundary` swaps to it (`UseArea` / `RestoreArea`), the minimap is photographed again for the smaller area, and the buoy ring is laid round the new edge.
- **Hard edge (new):** no autopilot turn-back. Past the edge the HUD counts down OUTSIDE THE ARENA — GET BACK IN 2.1, and after **3 s** outside the plane is out (`DeathCause.OutOfBounds`, breaks up in mid-air, OUT OF THE ARENA). Kill credit follows the crash rule (the last attacker, if recent). Bots turn back toward the middle on their own when they're heading out and will be within the edge margin in 3 s (`BotState.AvoidEdge`, above everything but terrain). The ceiling still pushes down softly.
- **Airfields:** each can be **closed** for the match (`Airfield.SetOpen`): still scenery, but nobody repairs or spawns there, its AA is off, and the minimap, nearest-field marker and RETURN TO BASE leave it out (with no field at all, RETURN TO BASE and the alarm don't show). Open fields keep their roles: home (spawn, repair, AA), neutral (nothing until captured). Capturing comes with Airfield Domination.
- **Spawns:** the usual airfields, or an **air start**: every plane appears on a ring in the air, level and facing the middle, in the free slot furthest from everyone already flying (`ArenaRules.SpreadSlot`), so a field of any size spreads round the whole ring. A mode's own spawn pose still comes first.
- **Bots:** the arena can give its own patrol points (`BotRoster.SetFightAreas`).

| Mode | Arena |
|---|---|
| Teams, Capture the Banner, Airfield Domination | The Island as built (Domination's extra fields and Banner's stands come with those modes) |
| Ram Royale | **700 m** round Lighthouse Rock (0, -1950), **450 m** ceiling, edge warning 150 m in, **hard edge**; every airfield closed; air start on a **550 m** ring at **220 m**, 16 slots; bots patrol over the lighthouse and four points 350 m round it at 200 m. Its north edge just reaches the south coast |

## Capture the Banner (prototype)

*Working name.* Teams, but the way to win is stealing the enemy's banner and bringing it home.

- **Size / length:** 8v8 with bots, 5 minutes. Island, normal boundary, normal respawns and repairs.
- **Banner stand:** each home airfield has a tall banner pole off the end of its runway, out past the runway's far end so the grab run isn't straight through all four AA guns. Its grab zone is a cylinder about 40 m across that only counts below about 40 m above the ground.
- **Grabbing:** fly an enemy pilot through the enemy stand's zone, low. No landing needed. The home AA still shoots, so a grab run is dangerous and needs escorts or a low, fast approach.
- **Carrying (the drag):** the banner trails on a long cable behind the plane, a big cloth streamer everyone can see. The carrier gets about −25% top speed, −30% climb and acceleration, and **no Boost or Afterburner** (a modifier, removed on drop). Barrel Roll and Armor Shield still work. The carrier shows on every enemy's minimap and HUD the whole time. It's a target, not a sneak.
- **Dropping:** the banner comes loose only when the carrier is **destroyed** (shot down or crashed). It falls on its cable and lies where it lands (it floats on the sea) for 20 s. An enemy pilot flying low through it grabs it again. A defender flying low through it sends it straight home. If nobody touches it, it returns home after 20 s.
- **Scoring a capture:** carry the banner low through **your own** stand's zone. A capture is worth **300** team points, plus the normal kill / assist points. **Proposed for a 5-minute match:** your own banner does **not** have to be at home to score, since the classic rule makes stalemates and the match is short. Try the classic rule in a playtest too.
- **Win:** most captures when time runs out, or **3 captures** ends it at once. Equal captures: the higher team score wins. Then a draw.
- **Carrier and repairs:** the carrier can't repair while carrying. Landing in a repair zone with the banner does nothing, so a hurt carrier has to choose: run home or get shot down.
- **HUD:** both banners' state at the top (HOME / TAKEN by X / DROPPED + return timer), a marker on each banner and carrier, a drag icon on the carrier's flight panel, and callouts ("WEST HAS YOUR BANNER", "BANNER RETURNED", "CAPTURED!").
- **Bots:** split roles. **Runners** (Interceptors and Fighters) take an approach lane in, grab and run home. **Escorts** form up on a friendly carrier. **Defenders** hunt the enemy carrier first and return a dropped banner. Everyone else plays Teams as usual.

## Airfield Domination (prototype)

*Working name.* Control the airfields. Capturing one means **landing on it**, and bombers open up defended fields. It makes players try the Heavy and practice landings.

- **Size / length:** 8v8 with bots, 5 minutes. Island.
- **Fields:** the two home fields stay as each team's spawn and can't be captured. The prototype has **3 capture fields**: Central plus a new pair of neutral fields placed as mirror images of each other, the same distance from both homes. A **5-field** version adds a second mirrored pair. Try both and keep the one that plays better. Each new field needs 2 approach lanes, and Central's landing direction must be settled first (the decision moves up from Phase 8).
- **Capturing (landing):** a plane that **lands and stops** inside a field's repair zone starts capturing it: **10 s** for one plane, faster with more (proposed: each extra plane is +50% speed, up to 3). An enemy plane also landed in the zone **contests** it, so progress stops. No invulnerability while capturing: a plane sitting on the runway is a target. Shooting it off the runway is how defenders fight back.
- **Defended fields (bomber role):** a field a team owns gets **2 light AA guns** and a **control tower**. While the tower stands, enemies capture it **3× slower**. Bombing the tower (a `GroundTarget`; about 2 bomb hits) shuts off that field's AA and repair and lets enemies capture at full speed. It rebuilds after 45 s. Domination AA guns can be destroyed (rockets or bombs) and rebuild after 30 s. Bombs make flipping a field possible, but someone still has to land.
- **Owned field benefits:** the owning team repairs there (normal invulnerability while the timer runs) and can respawn there. The class / respawn screen gets a spawn-point choice: home or any owned field.
- **Scoring:** a capture is worth **100** to the team plus 50 to each pilot who helped capture. Each owned field adds **5 points every 2 s**. Kills and assists score as in Teams. Highest score at 5:00 wins, or **holding every capture field for 30 s** wins at once.
- **HUD:** field letters (A / B / C …) in the owner's color at the top and on the minimap, a capture progress ring when landed in a zone, TOWER DOWN on a bombed field, and callouts ("CENTRAL CAPTURED BY EAST").
- **Bots:** each team splits between fields by value (neutral / enemy / threatened). A Heavy goes for the tower of a defended enemy field. Capture bots fly an approach lane and land using the existing retreat-to-repair landing code, and others fly cover over the field. Bots on the ground leave if an enemy lands too.

### Capture rules (built 2026-10-19)

Pure rules in `FieldBoard` (`Scripts/Match/DominationRules.cs`); nothing in the Island feeds them yet.

- **Who counts:** a plane parked in a capture field's zone: alive, on its wheels, stopped, not backing up (`FieldBoard.IsParked`). Home fields aren't on the board.
- **Speed:** 10 s for one plane; each extra plane of the same team adds +50%, counting up to 3 (6.7 s for two, 5 s for three or more). While the owner's control tower stands, 3× slower (30 s alone). A tower on a neutral field changes nothing.
- **Contested:** planes of two teams parked on the same field: progress holds where it is. When one side leaves, the other picks up where it stopped.
- **Nobody capturing** (empty, or only the owner's own planes): progress drains back at the one-plane rate. So a defender who lands and sits on their field undoes an enemy's half-capture.
- **Switching teams:** a team arriving on a field with the other team's progress first undoes it, then builds its own.
- **Flip:** at 100% the field goes straight to the capturer (no neutral step), and every capturing pilot parked there at that moment is on the capture (for the +50 each).
- **No perks for capturing:** the board gives the plane nothing, no repair and no invulnerability.

### Field defenses (built 2026-10-19)

`FieldDefenses` on each of the 3 capture fields, made by the Island builder from the airfield prefab's buildings.

- **Control tower:** the tower and its cab are one `GroundTarget`, **300 HP** (2 direct bomb hits), rebuilt **45 s** after it falls. While it's down the field has no AA and no repair (`Airfield.TowerDown`; `IsFriendlyTo` is false, so the RETURN TO BASE marker, bots' retreats and respawns skip it too), and `FieldDefenses.TowerUp` is false for the capture board's 3× slowdown.
- **Light AA:** of the prefab's four guns, the two on one diagonal stay (the others are removed), on lighter `LightAAGuns` stats (600 m, 3 rounds/s, 1.5 damage, wider spread). Each is a `GroundTarget`, **120 HP** (one bomb, or a few rocket hits: rockets now deal `RocketStats.groundDamage`, 40 at the center), rebuilt after **30 s**. A gun fires only while it's intact, the field is owned and its tower stands. Home fields' four guns are unchanged and can't be destroyed.
- **Changing hands:** a wreck stays a wreck when a field is captured (`Airfield.SetTeam`) and rebuilds on its own timer for the new owner. Home fields can't change hands.
- **Dormant outside Domination:** until a match calls `SetActive(true)` nothing can be hit and the AA stays quiet, so Teams and Banner see only scenery. Switching on or off rebuilds everything. The Domination world switches it on (below).

### Owned fields (built 2026-10-20)

`DominationMode` (Teams scoring for now, plus the `FieldBoard`) and `DominationWorld` (`Scripts/Match/DominationWorld.cs`), which `MatchManager` runs from the first frame of a Domination match.

- **Start:** every open, non-home field with `FieldDefenses` is a capture field (board order by name: Central, South East, South West). Each starts neutral and capturable (`Airfield.SetCapturable`) with its defenses on.
- **Capturing:** each frame the planes parked in a capture field's zone go to the board, along with whether each owner's tower stands. A field the board flips goes to the capturer (`Airfield.SetTeam`; the hangar and tower colors follow) and `DominationWorld.Captured` fires for the score and callouts that come next.
- **Landing:** the auto-brake now rolls a plane into the zone of a field it can capture, as it does on its own fields, so a capture landing stops in the right place.
- **Owned field benefits:** nothing new: a held field is friendly (`Airfield.IsFriendlyTo`), so the existing rules give its owner repair (with the usual invulnerability), RETURN TO BASE, the bots' retreats and respawns there. With the tower down none of that works, the same as for any field.
- **Respawn choice:** while dead, the picker shows a **SPAWN AT** row under the plane cards once there's more than one field to come back at: HOME first, then held fields by name. Click one or press TAB (Shift+TAB back). Default is the nearest friendly field to where you died; a pick that's lost before the timer runs out falls back to that. Bots respawn at the friendly field nearest where they died. The row is general (`Airfield.SpawnFieldsFor`), so in Teams it never shows.
- **Match over:** Dismiss, a mode change or leaving the scene puts every capture field back to neutral, not capturable, defenses dormant.
- The card stayed **IN DEVELOPMENT** until capture scoring, the HUD and the bots existed. All three are in (2026-10-21), so it's playable.

### Scoring (built 2026-10-20)

`DominationMode` (`Scripts/Match/MatchMode.cs`); `DominationWorld` passes it each frame's captures.

- **Capture:** 100 to the team ("Field captured", no pilot) plus 50 to the team and the personal score of every capturing pilot parked there when it flipped ("+50 CAPTURE" popup). Each of them gets a capture on the **CAP** scoreboard column (shared with Banner's `PilotScore.Captures`).
- **Holding:** every 2 s of play each team gets 5 per capture field it owns (team score only, no popup). Neutral fields pay nobody.
- **Kills / assists:** as in Teams (50 / 20).
- **Early win:** one team owning every capture field for 30 s straight ends the match (`EarlyWinner`). Losing any field, even to neutral, restarts the 30 s. `HoldingAll` / `HoldingAllTime` expose the clock for the HUD.
- **At time:** highest team score wins, a tie is a draw. The headline reads `WEST 420 – 380 EAST · FIELDS 2 – 1`.

### HUD (built 2026-10-21)

`FlightHUD.Domination.cs`, reading `DominationWorld` / `FieldBoard`; the text rules are in `HudText` (EditMode-tested).

- **Field bar:** a chip per capture field under the score bar, in board order (A Central, B South East, C South West): the letter in the owner's colour (grey when neutral), the field's name, and its state: the owner, `EAST 45%` with a bar in the capturing team's colour while a capture is under way, CONTESTED (amber, blinking), or TOWER DOWN (red, blinking) on an owned field.
- **Hold-every-field clock:** while one team owns them all, a line under the chips counts down: `WE HOLD EVERY FIELD · WIN IN 18 S` (green) or `EAST HOLDS EVERY FIELD · THEY WIN IN 18 S` (red, blinking).
- **Markers:** a ringed letter over each capture field in view, in the owner's colour, with its distance and TOWER DOWN. In view only (no edge arrows: the minimap and the RETURN TO BASE arrow already point the way), and hidden on the ground and on approach.
- **Minimap:** the letter above each capture field's square in the owner's colour, with a pulsing ring while it's being captured or contested.
- **Capture ring:** landed in a capture field's zone, a ring of dots round the screen centre fills with the capture in the capturing team's colour, the field's letter inside. Under it: STOP IN THE ZONE TO CAPTURE while still rolling, then CAPTURING 45% / UNDOING EAST 30% / CONTESTED: ENEMY ON THE FIELD / HELD BY YOUR TEAM, plus a note that a standing enemy tower makes it 3× slower.
- **Callouts** (shared with Banner's stack): YOU CAPTURED CENTRAL / WE CAPTURED CENTRAL / CENTRAL CAPTURED BY EAST; on an owned field's tower falling, CENTRAL TOWER DOWN: NO AA OR REPAIR (owner) or …: FULL-SPEED CAPTURE (everyone else), and TOWER REBUILT when it's back; WE HOLD EVERY FIELD: 30 S TO WIN / EAST HOLDS EVERY FIELD: TAKE ONE BACK.
- Not yet looked at in a screenshot.

### Bots (built 2026-10-21)

Capturers and cover by roster slot (4 / 3 in 7), split over the fields by value; bots with bombs go for defended enemy towers first; capturers fly an approach lane in and land; cover circles the field and strafes enemies parked on it; a parked capturer leaves when an enemy lands too. Details in [bots](../02-Mechanics/bots.md#airfield-domination-built-2026-10-21-prototype).

## Ram Royale (prototype)

*Working name.* A party mode: no weapons, planes hitting planes, one life each, last plane standing wins. Replaces the earlier sketch further down.

- **Size / length:** free for all, up to 16 (bots fill), one round of **up to 3 minutes**.
- **Arena:** a small circle so everyone stays close: proposed **~700 m radius, 50–450 m altitude**, over the open sea south of the island near Lighthouse Rock (few obstacles, and the lighthouse is something to weave around). The boundary here is a **hard edge**: flying past it is out (with a 3 s warning), unlike the soft turn-back in other modes. **Proposed:** in the last minute the circle shrinks to ~250 m.
- **Start:** everyone **spawns in the air already flying** at cruise, spread on a ring and facing roughly into the middle, after the 3-2-1. No runway, no airfields, no repairs.
- **Loadout:** **no guns, rockets, missiles or bombs, no Boost and no abilities** (playtest 2026-10-27: it's flying and ramming only). The class pick stays for its handling and ram mass.
- **Ram damage (new):** today a plane that sweeps into another plane just crashes, and the other plane takes nothing. Ram Royale needs a real plane-on-plane rule. Proposed: both planes take damage from the **closing speed** at the contact, to the parts that touched (a clipped wing comes off, a nose-on hit damages the body). The plane that hits **from behind or above** (more of the closing speed is its own) takes only ~30% of it, and a head-on hit hurts both fully, so meeting head-on usually kills both. Heavier classes deal more and take less. A crippled plane keeps flying, and body at 0 is out. The other plane gets kill credit, and a plane that crashes into the sea or the lighthouse within 10 s of a hit credits whoever hit it last (the existing crash credit).
- **Out:** a destroyed plane becomes a spectator: a follow camera cycling through the survivors, with a list of who's left.
- **Win:** the last plane flying. If time runs out, the plane with the most body HP left wins, then the most rams. **Scoring** (for XP / coins later): a placement bonus plus points per ram knock-out.
- **HUD:** planes left (e.g. 7 / 16), a big arrow or marker on the nearest plane, the shrinking-edge warning, and a ram hit flash ("RAMMED X!").
- **Bots:** a new ram behavior: steer at a **collision intercept point** (not the gun lead point), boost in the last second, and break away from anyone lining up on them. In this mode the existing plane-avoidance reflex only dodges planes that are coming at the bot.

### Free for all, one life, spectating (built 2026-10-21)

- **Framework:** `MatchMode.FreeForAllSize` (0 = the level's teams) and `MatchMode.Lives` (0 = unlimited; `CanRespawn` checks deaths against it), so any later mode (Free For All, Sky Racing) gets both by overriding two numbers.
- **Teams:** in a free-for-all mode `MatchManager` has the `BotRoster` fill **16 teams of 1** (`BotRoster.SetTeams`); the player stays team 0 and bots take teams 1–15, named `Bot 5 (Fighter)`. A team's name is its pilot's name, and each pilot has its own colour (`FreeForAll.PilotColor`: 16 hues, the first two are the usual West blue and East red). The level's 2 × 8 come back on Dismiss or when the scene goes. A bot with no home airfield appears wherever the match's spawn rules put it (the arena's air ring).
- **`RamRoyaleMode`** (`MatchMode.cs`, card still IN DEVELOPMENT): 16 pilots, one life, plays in the `RamRoyale` arena even off the card. For now a knock-out scores 100 to whoever gets the credit, with no assists; the score bar shows only the clock; scoreboard columns KO / RAMS / OUT / SCORE, pilots still flying listed first; headline "7 OF 16 STILL FLYING". Weapons are off (see [Loadout](#loadout-built-2026-10-21)) and ram damage is on (see [Ram damage](#ram-damage-built-2026-10-22)); the last plane flying wins (see [Win](#win-built-2026-10-22)).
- **Spectating:** a player out of lives gets no respawn picker, no flight HUD and no controls (`MatchManager.IsSpectating`). The camera holds on the explosion for **2.5 s**, then follows the survivor nearest it in a plain chase view (`FlightCameraRig.Spectate`); when that plane goes down it holds on that explosion and moves to the next nearest. ← → (or LB / RB, or the on-screen < > buttons) switch planes. The screen shows YOU'RE OUT, the watched pilot at the bottom and a STILL FLYING list on the right with each pilot's colour. The scoreboard ends it and hands the camera back.
- **Scoreboard (free for all):** one ranking split into two columns of 8, each row with its place and the pilot's colour dot.

### Loadout (built 2026-10-21)

- **Weapons off:** `RamRoyaleMode.OnSpawned` sets `PlaneController.WeaponsOff` on every plane it spawns (cleared on respawn, so other modes never see it). Guns, rockets, missiles and bombs then report `IsEquipped` false: the triggers do nothing, missiles don't lock (so nobody gets a lock warning), and the HUD's GUN / ammo rows, gun pippers and target brackets and the touch Fire / weapon buttons go away. Bots hold fire.
- **Abilities:** none, and no Boost (`PlaneController.AbilitiesOff`, set in `RamRoyaleMode.OnSpawned`; the HUD's BST meter, the ability slot and both touch buttons hide). Changed after the 2026-10-27 playtest; until then Support flew a **Ram Boost** (`RamBoostAbility`, still in the code for any mode that wants it through `PlaneAbility.Override`).
- **Classes:** the class pick stays. In a free-for-all the bots balance classes across the whole field by the usual 3 / 2 / 2 / 1 weights (6 Fighters, 4 Interceptors, 4 Heavies, 2 Supports in 16), instead of every team of one taking a Fighter.

### Ram damage (built 2026-10-22)

- **On / off:** `RamRoyaleMode.OnSpawned` sets `PlaneController.RamDamage` (cleared on respawn, like `WeaponsOff`). Other modes keep the old rule: the plane that sweeps into another crashes, or loses the wing or tail that clipped it.
- **The contact:** when a plane's collision sweep finds another plane, both planes take damage, are pushed apart, and then pass through each other for **1 s** so one contact counts once. The numbers are in `RamRules` (pure, on the plane's `PlaneController`, tunable per prefab).
- **How much:** **1.2 damage per m/s of closing speed** (the speed the two were closing at). Head-on at cruise is about 200 m/s, so both planes take ~240 and die. Under **8 m/s** it's a harmless bump.
- **Who hit whom:** a plane takes the full hit when it was in front of the other plane's nose (the other flew into it), and only **30%** when it hit the other from behind, above, below or the side, blending between the two over a ~45° cone (`headOnCone` 0.7). So a head-on hurts both fully, a hit from behind or above mostly hurts the plane that was hit, and a T-bone favours the plane that hit nose first.
- **Mass:** `FlightStats.ramMass` (Fighter 1, Interceptor 0.8, Heavy 1.6, Support 1.2) splits each hit as 2 × its mass ÷ both masses, so a Heavy deals twice what it takes from an Interceptor, and the lighter plane is pushed further. The push apart is 25% of the closing speed (at most 30 m/s), split by mass, with a roll away from the side that was hit.
- **Armor Shield** would halve ram damage taken, as with any damage, but abilities are off in this mode.
- **Parts:** the part that touched takes it: a wing or stabilizer (it comes off at 0, and any extra is lost), anything else (body, engine, nose) goes to the body. A part already gone passes the hit to the body. A crippled plane keeps flying; body at 0 is out.
- **Kill credit:** each plane's damage is credited to the other, so a head-on that kills both gives each a knock-out. The death cause is the new `DeathCause.Rammed`: the wreck breaks up in mid-air, the death line reads RAMMED BY X and the kill feed X RAMMED Y. A plane that crashes after a ram credits whoever rammed it (the usual crash credit: 20 s, or until repaired when crippled; there's no repair here, so a lost part keeps the credit for good).
- **Event:** `PlaneController.AnyRammed` (`RamInfo`: hitter, victim, point, damage each took, closing speed) for the HUD's ram flash (see [HUD](#hud-built-2026-10-22)) and the most-rams tiebreak (`MatchMode.RecordRam`, see [Win](#win-built-2026-10-22)).

### Win (built 2026-10-22)

- **Last plane flying:** `RamRoyaleMode.EarlyWinner` ends the match the moment one pilot is left (with at least two in the match), and that pilot wins. The headline reads "X · LAST PLANE FLYING".
- **Going down together:** everyone out on the same frame shares the best of their places, so a head-on between the last two puts both 1st; the one with more rams wins, and level rams is a **draw** (`EarlyWinner` returns `MatchScore.Draw`, which still ends the match).
- **At time (3:00):** among the planes still flying, the most **body HP** wins (absolute HP, so a Heavy's bigger body counts), then the most rams, else a draw. Body HP comes in through `RamRoyaleMode.BodyHealth` (`MatchManager` reads each plane's Body part).
- **Rams:** a contact counts a ram for each plane that did the other damage (a head-on counts for both; a harmless bump for neither), in `PilotScore.Rams`, fed from `AnyRammed` through the new `MatchMode.RecordRam` hook. Scoreboard column RAMS.
- **Places:** `PilotScore.Place` is given when a pilot goes out (how many still flying + 1). The scoreboard lists the flying by body HP, then the out by place, rams breaking ties, then score.
- **Not yet:** the placement bonus points (for XP / coins later); KO points stay 100 each.

### HUD (built 2026-10-22)

`FlightHUD.RamRoyale.cs`, on while a Ram Royale match is on (`match.Mode is RamRoyaleMode`); the edge warning works for any hard-edged arena.

- **Planes left:** the score bar shows PLANES LEFT 7 / 16 left of the clock, in place of the team scores (new `MatchWidgets.PlanesLeft`; flying = survivors plus the player if still in, out of `TeamCount`).
- **Plane markers:** with the guns off there are no gun brackets, so every plane in view within 1.5 km gets small corner brackets in its pilot's colour. The **nearest plane** gets a pulsing ring with its name and distance, and CLOSING 85 M/S while closing; out of view, a big arrow on the screen edge points at it with its name and distance.
- **INCOMING RAM · BOT 5:** a plane flying at us (at least 20 m/s of its own speed our way, so not one we're chasing) that will pass within 30 m in the next 2.5 s if both hold course, with an amber arrow round the crosshair pointing at it. Pure math in `HudMath.ClosestApproach` / `ClosingSpeed`.
- **Minimap:** every plane shows, in its pilot's colour (a small arena, nowhere to hide); other modes still show only friends and radar contacts.
- **Edge warning:** near a hard edge the line reads ARENA EDGE 120 M · TURN BACK (past it, the existing OUTSIDE THE ARENA countdown), a red arrow round the crosshair points back to the middle, and the screen's rim glows red, stronger the closer to the edge. The shrinking edge isn't built, so this is the fixed edge for now.
- **Ram flash:** from `PlaneController.AnyRammed` when we're in the contact: RAMMED BOT 5! (gold, rim flashes gold) when we flew into them or did at least as much as we took, RAMMED BY BOT 5 (red; the damage vignette already flashes) when they flew into us and we came off worse; nothing for a harmless bump. Under it 120 DEALT · 36 TAKEN; pops in, fades over 1.6 s. A part knocked off by a ram pops WING / TAIL / FIN KNOCKED OFF! (not SHOT OFF).
- **Fix found on the way:** at GO the air start used to bank the player hard away from the middle. During the frozen countdown the plane's interpolated transform still showed the hangar pose, the camera rig synced the mouse-aim direction to it, and at GO mouse aim steered back to the hangar's heading. The rig now reads the simulated pose (`PlaneController.Rotation`).

### Bots (built 2026-10-25)

Bots ram rather than shoot: full speed at the collision point (not the gun lead point). With Boost and abilities off here, their boost-in-the-last-second and ability uses (charge, shield, roll) stay idle. They break away (Barrel Roll if they have it) from a plane flying at them only when the hit would hurt them more than it, or kill them, so a head-on at cruise usually ends in both breaking. They stay inside the hard edge and under the ceiling. Details in [bots](../02-Mechanics/bots.md#ram-royale-built-2026-10-25-prototype).

- **Fixed on the way:** over the ceiling a plane's own autopilot takes the stick and held the heading, nose down, so near a hard edge it could fly a player out of the arena while they tried to turn back. Now, over the ceiling near (or past) a hard edge, the takeover also turns back toward the middle, a level turn at most 80° off the nose (`FlightBoundary.CeilingReturnPoint`), and hands the stick back heading in. Elsewhere it still just holds the heading, and under the ceiling a hard edge still never turns a plane back. Bots also keep under the ceiling.

## Sky Racing (prototype)

*Working name. Added 2026-10-20; design decided below, nothing built.* A kart-racer in the air, in the spirit of Diddy Kong Racing and Mario Kart: fly a lap course through gates, fly through **colored mystery boxes** to get an item, and use it on the pack. Finish first.

- **Scene:** its own `Race` scene with a purpose-built track (not the Island).
- **Size / length:** free for all, up to 8 racers (bots fill; fewer if the track is too tight for 8), **3 laps**, about 2–3 minutes a lap, with a cap on total time.
- **Planes:** everyone flies the main plane (the Fighter). Racers are told apart by the **color of the wingtips**.
- **Course (decided):** a **curvy circular loop**, lapped. An ordered loop of **gate rings** with a start/finish line. Gates must be flown through in order; a wrong-way arrow shows if you turn around, and a missed gate has to be flown before the lap counts.
- **Start:** a 3-2-1 grid start in the air on a staggered block. No runway, no airfields.
- **Mystery boxes (decided):** boxes float along the course, and **the color tells you the item**, as in Diddy Kong Racing: **blue = Boost**, **red = Homing Missile**, **green = Shield**. One item held at a time; the box returns after a few seconds. Use it with a button (secondary fire / ability on PC and gamepad, a touch button on mobile). **No position-weighted odds** for now.
- **Items (decided, only these three):**

| Item | Box | Effect | Source |
|---|---|---|---|
| Boost | Blue | A long burst of speed | Boost / Afterburner |
| Homing Missile | Red | Locks the plane ahead; the existing missile, **built first** | `PlaneMissiles` |
| Shield | Green | A short bubble that cuts damage | Armor Shield |

- **Weapons:** guns, rockets and bombs are off; only the collected items hurt.
- **Damage (decided):** the normal part-damage system applies, so a missile hit can shoot off a part and the plane flies on damaged. **Only a crash respawns you** (terrain, water, obstacle, or destroyed): close to where you were in the race (last gate passed, facing the next), after **up to 3 s**, fully repaired. A lost wing or other part is not a respawn: you race on damaged. No repair stops.
- **Scoring:** finishing order gives placement points (for XP / coins later). Best lap time is shown.
- **HUD:** position (3 / 8), lap, next-gate marker and arrow, held item, minimap with the track, lap times, wrong-way warning.
- **Bots:** follow a racing line through the gates, grab boxes, and use items (missile at the plane ahead, shield with a missile inbound, boost on a straight). Difficulty changes line quality and item use.

## King of the Sky

Capture and hold airspace zones.

- Zones are 3D volumes in the sky (cylinders or spheres).
- Holding a zone ticks score for your team.
- Zones could rotate over time to move the fight around the map.

## Carrier War

Each team has an aircraft carrier at sea. Destroy the enemy carrier to win.

- Carriers are both the spawn point and the repair zone.
- Bombs and rockets do bonus damage to carriers.
- Landing on a moving carrier is harder, which raises the skill ceiling for repairs.

## Ram Royale (Secondary Loop): earlier sketch

*Working name.* Players crash into each other, and the last plane standing wins. Superseded by [Ram Royale (prototype)](#ram-royale-prototype).

- No guns, or guns disabled
- No repairs
- A shrinking play area to force contact (proposed)
- Short rounds (~2–3 min), meant as a party or warm-up mode

## Mode Comparison

| Mode | Teams | Repair | Primary focus |
|---|---|---|---|
| Free For All | No | Neutral airfields | Kills |
| Teams | 2 | Home + captured | Kills + objectives |
| Capture the Banner | 2 | Home | Steal and carry the banner |
| Airfield Domination | 2 | Home + owned fields | Land to capture, bomb to open |
| King of the Sky | 2 | Home | Zone control |
| Carrier War | 2 | Carrier | Destroy carrier |
| Ram Royale | No | No | Survival (one life, no weapons) |
| Sky Racing | No | No | Lap race, mystery-box items |

## Open Questions

- [x] Players per match: up to 16
- [x] Match length: ~5 minutes (Ram Royale shorter)
- [x] First mode: Teams
- [x] Overtime / tie-breaker rules: none for now; a tie is a draw (2026-10-17)
- [x] Capture the Banner: does your own banner have to be home to score? → **no** (the proposed rule; drag strength and stand AA still to tune in a playtest).
- [ ] Capture the Banner, still open: how strong is the drag? Should home AA stay on around the stand?
- [ ] Airfield Domination: **3 capture fields, decided 2026-10-19** (Central plus South West / South East at (±750, −900), one-way, landing north). Should landing be the only way to capture, or should a low flyover also start a slow capture?
- [ ] Ram Royale: arena size and whether it shrinks; class pick or one plane for all; ram damage split (rear hit vs head-on).
- [ ] Sky Racing, still open: track layout and theme, how many boxes and where, and mobile item aiming.
- [ ] Which prototype modes ship at launch, which go to Later, and which get cut (decide after the Phase 6.5 playtest).
