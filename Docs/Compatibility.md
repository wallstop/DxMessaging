# Compatibility

DxMessaging is render‑pipeline agnostic (pure C#) and targets Unity 2021.3+. The matrix below summarizes support by Unity version and Render Pipeline.

Unity Version vs Render Pipeline

| Unity      | Built‑In RP   | URP           | HDRP          |
| ---------- | ------------- | ------------- | ------------- |
| 2021.3 LTS | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 2022.3 LTS | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 2023.x     | ✅ Compatible | ✅ Compatible | ✅ Compatible |
| 6.x        | ✅ Compatible | ✅ Compatible | ✅ Compatible |

Notes

- RP‑agnostic: DxMessaging does not depend on rendering APIs; it works equally across Built‑In, URP, and HDRP.
- Minimum version is governed by the package manifest (`unity`: 2021.3). Newer LTS versions are expected to work.

## Architecture Pattern Compatibility

### Scriptable Object Architecture (SOA)

DxMessaging can work alongside Scriptable Object Architecture patterns, though SOA has documented limitations. See [Pattern 14: SOA Compatibility](Patterns.md#14-compatibility-with-scriptable-object-architecture-soa) for detailed integration strategies, code examples, and migration paths.

#### Quick summary

- ✅ **Compatible** - DxMessaging can bridge with SOA systems
- ⚠️ **Not recommended** - SOA has scalability and maintainability concerns ([detailed critique](https://github.com/cathei/AntiScriptableObjectArchitecture))
- ✅ **Best practice** - Use ScriptableObjects for immutable design data, DxMessaging for runtime events
- → See [SOA Integration Patterns](Patterns.md#14-compatibility-with-scriptable-object-architecture-soa) for three coexistence strategies with code examples

### Dependency Injection (DI) Frameworks

DxMessaging integrates seamlessly with popular DI frameworks:

- **Zenject** - See [Zenject Integration Guide](Integrations/Zenject.md)
- **VContainer** - See [VContainer Integration Guide](Integrations/VContainer.md)
- **Reflex** - See [Reflex Integration Guide](Integrations/Reflex.md)

DI and DxMessaging complement each other: DI manages dependencies/services, DxMessaging handles event communication.

### Other Unity Frameworks

For comparisons with other messaging/event frameworks (UniRx, MessagePipe, Zenject Signals, etc.), see [Framework Comparisons](Comparisons.md).
