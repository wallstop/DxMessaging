# Install (Unity UPM)

This page helps you install DxMessaging into a Unity 2021.3+ project using the Unity Package Manager (UPM).

Fast path

- Unity Package Manager > Add package from git URL...
- Paste:
  ```
  https://github.com/wallstop/DxMessaging.git?path=/Packages/com.wallstop-studios.dxmessaging
  ```
- Click Add. Unity imports the package and its analyzers/generators.

Manifest.json (alternative)

- Open your project’s `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.wallstop-studios.dxmessaging": "https://github.com/wallstop/DxMessaging.git?path=/Packages/com.wallstop-studios.dxmessaging"
  }
}
```

Minimum requirements

- Unity 2021.3+ (LTS recommended). See [Compatibility](Compatibility.md) for Unity × Render Pipeline support (Built‑In, URP, HDRP).

After install

- In your project, create a GameObject and add `MessagingComponent` to start sending/receiving.
- Optional: enable diagnostics in Editor from the MessagingComponent inspector to see live emissions.
- Jump to [Quick Start](QuickStart.md)
