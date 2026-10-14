# Multiplayer (Technical Notes)

Status: Draft
Last updated: 2026-10-13

## Summary

**Dedicated servers (decided).** Players connect to a game server that runs the match and is authoritative over everything: flight, hits, part damage, score, and coin payouts. Nobody plays on another player's machine.

## Known Requirements

- Full 3D, real-time aerial combat with fast-moving planes
- **Up to 16 players** per match, **~5-minute** matches, quick requeue
- **PC and mobile with cross-play**
- **Per-part damage** (body, wings, tail, engine) synced to all clients
- AI on the server: bots, convoys, AA guns
- Persistent accounts: coins, owned cosmetics, player level
- Free-to-play with in-app purchases
- **Solo, part-time developer:** minimize ops work and keep server costs under control

## Architecture

```
 Clients (PC / Mobile)
      │  inputs ▲ state snapshots
      ▼         │
 Dedicated Game Server (headless Unity build, Linux)
      │  match results (score → coins)
      ▼
 Backend services: Auth · Cloud Save · Economy · Matchmaker · IAP validation
```

- **Server-authoritative:** clients send inputs; the server simulates the planes, resolves hits per part, and broadcasts state.
- **Client prediction** for your own plane, plus interpolation for others.
- **Lag-compensated hit detection** on the server, with per-part hitboxes.
- **Bots run on the server** to fill empty slots.
- **Coin payouts come from the server** when a match ends, never from a client.

## Decisions Needed

| Decision | Options | Notes |
|---|---|---|
| Netcode library | Netcode for GameObjects (NGO), Fish-Networking, Photon Fusion (server mode) | All support dedicated servers. NGO is first-party; Fusion has the strongest prediction |
| Server hosting | Edgegap, AWS GameLift, PlayFab Multiplayer Servers, Unity's hosting partners | Check current offerings and pricing at decision time |
| Matchmaking | Unity Matchmaker, the hosting provider's own matchmaker | Quick-join into 8v8 |
| Accounts / save / economy | Unity Authentication + Cloud Save + Economy, or PlayFab | Must work cross-platform |
| IAP | Unity IAP (App Store, Google Play, PC store) | Server-side receipt validation |
| Ads | Unity LevelPlay (rewarded, mobile) | |

## Cost Notes

Dedicated servers cost money while matches are running. For a free-to-play solo project:
- Use **scale-to-zero / on-demand** hosting, so you pay only for active matches.
- Keep the server build lean (headless, no rendering, low tick rate where possible: ~30 Hz).
- For development, run the server locally (or one cheap VM) instead of on paid hosting.

## Recommended Build Order

1. Offline: flight model + per-part damage + repair loop, with bots.
2. Local dedicated server + 2 clients on one PC.
3. Hosted server + matchmaking.
4. Accounts, coins, prize machine.
5. IAP + ads.

## Mobile Considerations

- URP with mobile-friendly quality tiers
- Modest bandwidth: 16 planes × (position, rotation, inputs, part-state bitmask + part HP)
- Aim assist tuned for cross-play fairness

## Open Questions

- [ ] Which netcode library?
- [ ] Which hosting provider?
- [ ] Server regions at launch?
- [ ] Account linking (start anonymous, link later to a platform account?)
