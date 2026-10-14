# Economy & Monetization

Status: Draft
Last updated: 2026-10-13

## Summary

**Free to play (decided).** The only currency is **coins**. Players earn coins at the end of every match (a better score earns more), then spend them on a **random cosmetic prize machine**, like Crossy Road. Revenue comes from **cosmetic sales, coin sales, and rewarded ads that give coins**. Nothing for sale affects gameplay power.

## Goals

- Every match feels rewarding, even a loss.
- Short, simple loop: play → coins → spin → show it off.
- No pay-to-win. All planes of a class are equal for everyone.
- Monetization never interrupts a match.

## Coins

### Earning

| Source | Coins | Notes |
|---|---|---|
| Finish a match | Base amount (TBD) | Always paid, so losing still progresses |
| Match score | Scales with personal score | Kills, assists, objectives, heals |
| Win bonus | TBD | |
| Rewarded ad | TBD (e.g. double the match payout) | Optional, mobile |
| Free gift timer | TBD | Crossy Road-style: free coins every few hours (proposed) |
| Coin purchase | Store packs | Real money |

**Proposed formula:** `coins = base + floor(score × rate) (+ win bonus)`. Show the breakdown on the post-match screen.

### Spending: Prize Machine

- Fixed price per spin (TBD, e.g. 100 coins).
- Each spin awards **one random cosmetic** from the pool.
- Fun reveal animation (a crate drops from a plane, parachutes down, and pops open).

**Proposed duplicate rule:** a duplicate converts into a partial coin refund, or the pool only rolls items the player doesn't own yet (Crossy Road style). The second is friendlier and simpler.

## Monetization

| Stream | How | Platforms |
|---|---|---|
| Cosmetic sales | Direct-purchase shop for featured or premium cosmetics (not random) | All |
| Coin sales | Real-money coin packs | All |
| Rewarded ads | Watch an ad for coins (e.g. double the post-match coins) | Mobile; PC has limited ad support, so the PC equivalent is the free gift timer |
| Ads during matches | **None** | — |

**Rules:**
- No forced/interstitial ads during or between matches (proposed). Ads are always opt-in.
- Nothing purchasable affects stats.

## ⚠ Loot Box Regulation

Selling coins that can then be spent on **random** cosmetics makes the prize machine a **loot box** under many rules:

- **Apple and Google** require displaying the **odds** for each item before purchase.
- **Belgium** effectively bans paid loot boxes, and other regions are tightening rules.
- **Age ratings:** PEGI/ESRB add a "In-Game Purchases (Includes Random Items)" label.

Mitigations to plan for:
1. Show drop odds in the prize machine UI.
2. Duplicate protection (no-dupe pool), which makes "you'll get everything eventually" honest.
3. Always offer a **direct-buy** shop alongside the random machine.
4. Region flag to disable buying coins with real money (or the prize machine) where required.
5. Get a quick legal/platform-policy check before launch.

## Technical Needs

- Player accounts + cloud save (coin balance, owned cosmetics), across platforms
- **Server-side** coin grants and purchase validation; the client never decides how many coins it earned
- In-app purchases: Unity IAP (Apple, Google, plus a PC storefront, e.g. Steam)
- Rewarded ads: Unity LevelPlay (mobile)
- See [multiplayer.md](../08-Technical/multiplayer.md)

## Open Questions

- [ ] Spin price and coins per match (target: one spin every ~2–3 matches?)
- [ ] Duplicate rule: no-dupe pool or coin refund?
- [ ] PC storefront: Steam, itch, Epic?
- [ ] Rarity tiers for cosmetics (common / rare / epic)?
- [ ] Does player level still matter? (Proposed: display + a coin bonus on level up)
