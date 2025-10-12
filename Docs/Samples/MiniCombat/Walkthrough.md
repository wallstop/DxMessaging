# Mini Combat Walkthrough

This walkthrough explains how the sample scripts collaborate at runtime.

Scripts

- Messages.cs — defines `VideoSettingsChanged` (untargeted), `Heal` (targeted), and `TookDamage` (broadcast).
- Player.cs — registers to receive `Heal` targeted at its Component.
- Enemy.cs — emits `TookDamage` as a broadcast from its GameObject.
- UIOverlay.cs — listens to any `TookDamage` (all sources) and untargeted settings changes.
- Boot.cs — simulates a flow at startup.

Flow

- Boot emits a `VideoSettingsChanged` untargeted message → UIOverlay rebuilds its UI.
- Boot emits a `Heal` targeted to the Player component → Player increments HP.
- Enemy emits a `TookDamage` broadcast from its GameObject → UIOverlay observes it via `RegisterBroadcastWithoutSource`.

Targeting decisions

- Player uses Component‑targeted registration so only that script instance handles `Heal`.
- Enemy emits from the GameObject, which is fine because UIOverlay listens across all sources; if you wanted to constrain by source, register with `RegisterGameObjectBroadcast` or `RegisterComponentBroadcast`.

Tips

- Keep Player, Enemy, and UIOverlay GameObjects each with a `MessagingComponent`.
- Enable diagnostics in Editor on the MessagingComponent to inspect emissions and registrations while testing this sample.
