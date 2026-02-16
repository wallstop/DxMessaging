# DI Framework Integrations

DxMessaging integrates seamlessly with popular Unity dependency injection (DI) frameworks. This section covers how to combine DxMessaging's reactive event system with constructor-based dependency injection for a powerful, scalable architecture.

## Why Combine DI + Messaging?

Use the best of both worlds:

- **Constructor Injection** — for service dependencies (repositories, managers, configuration)
- **Messaging** — for reactive events (damage taken, item collected, game state changes)

This combination provides:

- Explicit dependencies with testable constructors
- Loose coupling between systems via events
- Scoped message buses for scene isolation
- Clean separation of concerns

## Supported Frameworks

### [VContainer](vcontainer.md)

Fast, lightweight DI with minimal overhead. Features scoped lifetimes for per-scene message buses.

```csharp
builder.Register<IMessageBus>(Lifetime.Scoped)
    .AsImplementedInterfaces()
    .AsSelf();
```

### [Zenject](zenject.md)

Feature-rich DI with extensive Unity integration. Supports complex binding scenarios and sub-containers.

```csharp
Container.Bind<IMessageBus>()
    .FromInstance(MessageHandler.MessageBus)
    .AsSingle();
```

### [Reflex](reflex.md)

Modern, reflection-free DI designed for performance. Zero-allocation resolution with AOT support.

```csharp
Container.Singleton<IMessageBus>(MessageHandler.MessageBus);
```

## Choosing a Framework

| Framework      | Best For                                     | Performance    |
| -------------- | -------------------------------------------- | -------------- |
| **VContainer** | Most projects, additive scenes               | ⭐⭐⭐ Fast    |
| **Zenject**    | Complex projects, existing Zenject codebases | ⭐⭐ Good      |
| **Reflex**     | Performance-critical, AOT platforms          | ⭐⭐⭐ Fastest |

## Common Patterns

All frameworks support the same core patterns:

1. **Global Bus** — Share `MessageHandler.MessageBus` across all systems
1. **Scoped Bus** — Create per-scene buses for isolation
1. **Builder Injection** — Inject `IMessageRegistrationBuilder` for flexible handler registration
1. **Testable Design** — Mock `IMessageBus` in unit tests

See each framework's guide for detailed examples and best practices.
