---
title: Home
description: High-performance type-safe messaging library for Unity
---

# DxMessaging Documentation

**DxMessaging** is a high-performance, type-safe messaging library for Unity that provides a clean, decoupled communication pattern between game components.

**[Get Started](getting-started/index.md)** | [View on GitHub](https://github.com/wallstop/DxMessaging)

## Why DxMessaging?

- ⚡ **High Performance** — Zero-allocation message dispatch with pooled handlers
- 🛡️ **Type-Safe** — Compile-time message type checking prevents runtime errors
- 📦 **Decoupled Architecture** — Components communicate without direct references
- 🎯 **Flexible Targeting** — Untargeted, Targeted, and Broadcast message patterns
- 🔧 **Unity-Native** — Built specifically for Unity with MonoBehaviour integration

## Quick Links

- **[Mental Model](concepts/mental-model.md)** — How to think about DxMessaging
- **[Visual Guide](getting-started/visual-guide.md)** — Beginner-friendly introduction with diagrams
- **[Quick Start](getting-started/quick-start.md)** — Your first message in 5 minutes
- **[Message Types](concepts/message-types.md)** — Untargeted, Targeted, Broadcast patterns
- **[API Reference](reference/reference.md)** — Complete API documentation

## Installation

### Via OpenUPM (Recommended)

```bash
openupm add com.wallstop-studios.dxmessaging
```

#### Or via Git URL

```text
https://github.com/wallstop/DxMessaging.git
```

See the [Install Guide](getting-started/install.md) for all options including NPM scoped registries and local tarballs.

## Quick Example

```csharp
// Define a message
public readonly struct DamageMessage : IUntargetedMessage
{
    public readonly int amount;
    public DamageMessage(int amount) => this.amount = amount;
}

// Subscribe and handle (using a MessageRegistrationToken)
_ = Token.RegisterUntargeted<DamageMessage>(msg =>
    Debug.Log($"Received {msg.amount} damage!"));

// Emit the message
DamageMessage damage = new DamageMessage(25);
damage.EmitUntargeted();
```
