# String Messages (Prototyping & Debugging)

Sometimes you just want to fire a quick string without defining a formal message. DxMessaging provides two built‑in types:

- `StringMessage`: targeted string message (to a specific recipient)
- `GlobalStringMessage`: untargeted string message (global broadcast)

When to use

- Rapid prototyping, debugging, tool scripts, or test spikes.
- Sending textual notifications without defining a struct.

When not to use

- Shipping gameplay code that benefits from compile‑time safety and structure. Prefer explicit message structs.

Examples

Global (untargeted)

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Extensions;

var msg = new GlobalStringMessage("Saved");
msg.Emit();
```

Targeted (to a specific GameObject)

```csharp
using DxMessaging.Core.Messages;
using DxMessaging.Core.Extensions;
using UnityEngine;

var msg = new StringMessage("Hello!");
msg.EmitGameObjectTargeted(gameObject);
// Or even
"Hello!".EmitGameObjectTargeted(gameObject);
```

Listening

```csharp
using DxMessaging.Core.Messages;

_ = token.RegisterUntargeted<GlobalStringMessage>(OnGlobalText);
_ = token.RegisterComponentTargeted<StringMessage>(this, OnTextToMe);

void OnGlobalText(ref GlobalStringMessage m) => Debug.Log($"Global: {m.message}");
void OnTextToMe(ref StringMessage m)       => Debug.Log($"To me: {m.message}");
```

Tips

- Strings are great for glue and debugging; convert hot paths to typed messages for performance and clarity.
- You can combine with interceptors/post‑processors for logging and filtering.

Related

- [Message Types](Docs/MessageTypes.md)
- [Diagnostics](Docs/Diagnostics.md)
