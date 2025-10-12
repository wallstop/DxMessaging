# Overview

[← Back to Index](Index.md) | [Getting Started](GettingStarted.md) | [Quick Start](QuickStart.md) | [Visual Guide](VisualGuide.md)

---

DxMessaging is a high‑performance, type‑safe messaging system for Unity and .NET that decouples producers and consumers without sprawling events or brittle global hooks. It gives you clear message categories, predictable ordering, and tooling to observe, intercept, and diagnose message flows.

What it solves

- Decoupling without tight references: producers don’t need to know consumers.
- Predictable lifecycle: explicit registration tokens that you enable/disable with Unity components.
- Ergonomics and performance: struct‑friendly APIs, by‑ref handlers, zero‑boxing patterns.
- Observability: interceptors, post‑processors, diagnostics buffers, and registration logs.
- Scalable message taxonomy: Untargeted, Targeted, and Broadcast messages fit most gameplay/UI flows.

When to consider DxMessaging

- You’re fighting complex webs of C# events/delegates with manual attach/detach and order issues.
- You need to decouple systems that span scenes or don’t share direct references.
- You want to model Gameplay/UI flows as messages with clear ownership (global vs to one entity vs from one entity).
- You need to inspect, validate, and sometimes cancel or normalize emissions.

Core ideas

- Message categories
  - Untargeted: Global notifications (e.g., settings changed).
  - Targeted: Sent to one specific target (e.g., Heal(10) to Player).
  - Broadcast: Emitted from a source; anyone can observe (e.g., TookDamage from Enemy).
- Ordering: Lower priority runs earlier; same priority uses registration order.
- Pipeline: Interceptors → Handlers → Post‑Processors, with diagnostics optionally enabled.
- Unity integration: `MessagingComponent` and `MessageAwareComponent` manage lifecycles cleanly.

Killer features

- **🚀 Global Observers: Listen to ALL messages** — Subscribe to all targeted/broadcast messages of a type without knowing specific targets/sources. **Unlike traditional event buses** where you need separate subscriptions per entity type (PlayerDamaged, EnemyDamaged, etc.), DxMessaging lets you subscribe ONCE to ALL damage events and get the source/target as a parameter. Perfect for analytics, debugging, achievements, and combat logs.
- **Priority ordering** and explicit pipeline stages (interceptors, handlers, post‑processors).
- **Local bus islands** for test isolation and modular subsystems.
- **Struct‑friendly, by‑ref handlers** to avoid boxing and copies.
- **Attributes + source generation** (`DxAutoConstructor`) reduce boilerplate while keeping strong typing.
- **Unity‑first helpers** (GameObject/Component emit) and a powerful MessagingComponent inspector.
- **Global accept‑all** for building inspectors and profilers.

---

## Related Documentation

**Start Here:**

- → [Visual Guide](VisualGuide.md) (5 min) — Beginner-friendly introduction
- → [Getting Started](GettingStarted.md) (10 min) — Complete guide
- → [Quick Start](QuickStart.md) (5 min) — Working example

**Go Deeper:**

- → [Message Types](MessageTypes.md) — When to use Untargeted/Targeted/Broadcast
- → [Comparisons](Comparisons.md) — DxMessaging vs alternatives
- → [Design & Architecture](DesignAndArchitecture.md) — How it works

**Install & Setup:**

- → [Install](Install.md) — Installation guide
- → [Compatibility](Compatibility.md) — Unity versions
