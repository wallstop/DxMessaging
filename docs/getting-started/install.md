# Install (Unity UPM)

This page helps you install DxMessaging into a Unity 2021.3+ project using the Unity Package Manager (UPM).

## Quick Reference

| Method                       | Command/URL                                    | Auto-Updates |
| ---------------------------- | ---------------------------------------------- | ------------ |
| **OpenUPM** (Recommended)    | `openupm add com.wallstop-studios.dxmessaging` | Yes          |
| **Git URL**                  | `https://github.com/wallstop/DxMessaging.git`  | No           |
| **NPM Scoped Registry**      | Add registry + resolve                         | Yes          |
| **From Releases**            | Download .unitypackage                         | No           |
| **From Source**              | Clone/download zip                             | No           |
| **Manual** (not recommended) | Edit manifest.json                             | No           |

## Methods

### OpenUPM (Recommended)

The easiest way to install DxMessaging is via [OpenUPM](https://openupm.com/packages/com.wallstop-studios.dxmessaging/).

If you have the [openupm-cli](https://openupm.com/docs/getting-started.html) installed, run:

```bash
openupm add com.wallstop-studios.dxmessaging
```

#### Benefits of OpenUPM

- Automatic update notifications in Unity Package Manager
- Easy dependency management
- Simple one-command installation
- Version history and changelogs available on the [OpenUPM page](https://openupm.com/packages/com.wallstop-studios.dxmessaging/)

**Don't want to install the CLI?** You can also [add OpenUPM as a scoped registry manually](https://openupm.com/docs/getting-started.html).

### Git URL

- Unity Package Manager > Add package from git URL...
- Paste:

```text
https://github.com/wallstop/DxMessaging.git
```

- Click Add. Unity imports the package and its analyzers/generators.

### NPM Scoped Registry

1. Open Unity Package Manager
1. (Optional) Enable Pre-release packages to get the latest, cutting-edge builds
1. Open the Advanced Package Settings
1. Add an entry for a new "Scoped Registry"
   - Name: `NPM`
   - URL: `https://registry.npmjs.org`
   - Scope(s): `com.wallstop-studios.dxmessaging`
1. Resolve the latest `DxMessaging`

Unity will notify you of version updates when using scoped registries.

### From Releases

Check out the latest [Releases](https://github.com/wallstop/DxMessaging/releases) to grab the Unity Package and import to your project.

### From Source

Grab a copy of this repo (either `git clone` [this repo](https://github.com/wallstop/DxMessaging) or [download a zip of the source](https://github.com/wallstop/DxMessaging/archive/refs/heads/master.zip)) and copy the contents to your project's `Assets` directory.

### Manual - Manifest.json (not recommended)

- Open your project's `Packages/manifest.json` and add:

```json
{
  "dependencies": {
    "com.wallstop-studios.dxmessaging": "https://github.com/wallstop/DxMessaging.git"
  }
}
```

## Minimum Requirements

- Unity 2021.3+ (LTS recommended). See [Compatibility](../reference/compatibility.md) for Unity × Render Pipeline support (Built‑In, URP, HDRP).

## After Installation

- In your project, create a GameObject and add `MessagingComponent` to start sending/receiving.
- Optional: enable diagnostics in Editor from the MessagingComponent inspector to see live emissions.
- Jump to [Quick Start](quick-start.md)
