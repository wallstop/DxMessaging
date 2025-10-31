# Install (Unity UPM)

This page helps you install DxMessaging into a Unity 2021.3+ project using the Unity Package Manager (UPM).

## Methods

### Fast path

- Unity Package Manager > Add package from git URL...
- Paste:

```bash
# Unity UPM: Add package from git URL...
https://github.com/wallstop/DxMessaging.git
```

- Click Add. Unity imports the package and its analyzers/generators.

### To Install as Unity Package

1. Open Unity Package Manager
1. (Optional) Enable Pre-release packages to get the latest, cutting-edge builds
1. Open the Advanced Package Settings
1. Add an entry for a new "Scoped Registry"
   - Name: `NPM`
   - URL: `https://registry.npmjs.org`
   - Scope(s): `com.wallstop-studios.dxmessaging`
1. Resolve the latest `DxMessaging`

⭐ Bonus of Unity Package way - Unity will tell you of version updates

### From Releases

Check out the latest [Releases](https://github.com/wallstop/DxMessaging/releases) to grab the Unity Package and import to your project.

### From Source

Grab a copy of this repo (either `git clone` [this repo](https://github.com/wallstop/DxMessaging) or [download a zip of the source](https://github.com/wallstop/DxMessaging/archive/refs/heads/master.zip)) and copy the contents to your project's `Assets` directory.

### Manual - Manifest.json (alternative, not recommended)

- Open your project’s `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.wallstop-studios.dxmessaging": "https://github.com/wallstop/DxMessaging.git"
  }
}
```

## Minimum requirements

- Unity 2021.3+ (LTS recommended). See [Compatibility](Compatibility.md) for Unity × Render Pipeline support (Built‑In, URP, HDRP).

## After install

- In your project, create a GameObject and add `MessagingComponent` to start sending/receiving.
- Optional: enable diagnostics in Editor from the MessagingComponent inspector to see live emissions.
- Jump to [Quick Start](QuickStart.md)
