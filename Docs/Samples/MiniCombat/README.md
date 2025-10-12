# Mini Combat Sample

This mini sample shows a small combat loop using DxMessaging with Unity‑friendly APIs.

Files

- Messages.cs — Untargeted, Targeted, Broadcast messages
- Player.cs — listens for `Heal` (Targeted)
- Enemy.cs — emits `TookDamage` (Broadcast)
- UIOverlay.cs — listens to settings (Untargeted) and any `TookDamage` (Broadcast across all sources)
- Boot.cs — quick driver to emit sample messages

Usage

- Drop these scripts into a Unity project (or skim the code here) and attach:
  - Player to a player GameObject (with MessagingComponent)
  - Enemy to an enemy GameObject (with MessagingComponent)
  - UIOverlay to a UI GameObject (with MessagingComponent)
  - Boot to any GameObject to simulate interactions

Notes

- For clarity, each script manages its own `MessageRegistrationToken` via `MessagingComponent.Create(this)`.
- Handlers are named methods; emits use GameObject/Component helpers.
- Assembly definition: if you place these scripts under a separate assembly, add an `.asmdef` that references the package assembly `WallstopStudios.DxMessaging` so `using DxMessaging.*` resolves.
- This sample is not part of the package build; it’s for learning and integration.

Walkthrough

- Read the full walkthrough: [Mini Combat Walkthrough](Walkthrough.md)
