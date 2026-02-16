---
title: "Singleton Usage Examples"
id: "singleton-usage-examples"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/Singletons/RuntimeSingleton.cs"
    - path: "Runtime/Core/Singletons/ScriptableObjectSingleton.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "unity"
  - "singleton"
  - "patterns"

complexity:
  level: "intermediate"
  reasoning: "Covers Unity-specific lifecycle and global access patterns"

impact:
  performance:
    rating: "low"
    details: "Singletons are about lifecycle management, not raw speed"
  maintainability:
    rating: "medium"
    details: "Global state needs discipline to avoid coupling"
  testability:
    rating: "medium"
    details: "Requires reset hooks or abstraction for tests"

prerequisites:
  - "Understanding of Unity lifecycle"

dependencies:
  packages: []
  skills:
    - "singleton-patterns"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity singleton patterns"

related:
  - "singleton-patterns"

status: "stable"
---

# Singleton Usage Examples

> **One-line summary**: Usage scenarios for runtime, ScriptableObject, and scene-scoped singletons.

## Overview

This skill provides applied singleton usage scenarios.

## Solution

Use these examples to integrate runtime and ScriptableObject singletons.

## Usage

### RuntimeSingleton Example

```csharp
public class AudioManager : RuntimeSingleton<AudioManager>
{
    private AudioSource musicSource;

    protected override void OnSingletonAwake()
    {
        musicSource = gameObject.AddComponent<AudioSource>();
    }

    public void PlayMusic(AudioClip clip)
    {
        musicSource.clip = clip;
        musicSource.Play();
    }
}

// Usage anywhere:
AudioManager.Instance.PlayMusic(myClip);
```

### ScriptableObjectSingleton Example

```csharp
[ScriptableSingletonPath("Config/GameSettings")]
[CreateAssetMenu(menuName = "Config/Game Settings")]
public class GameSettings : ScriptableObjectSingleton<GameSettings>
{
    [SerializeField] private float masterVolume = 1f;
    [SerializeField] private int targetFrameRate = 60;
    [SerializeField] private bool showTutorial = true;

    public float MasterVolume => masterVolume;
    public int TargetFrameRate => targetFrameRate;
    public bool ShowTutorial => showTutorial;
}

// Usage:
Application.targetFrameRate = GameSettings.Instance.TargetFrameRate;
```

### Auto-Loading Singleton

```csharp
[AutoLoadSingleton]
public class GameManager : RuntimeSingleton<GameManager>
{
    protected override void OnSingletonAwake()
    {
        Debug.Log("GameManager auto-loaded!");
    }
}
```

### Scene-Scoped Singleton

```csharp
public class LevelManager : RuntimeSingleton<LevelManager>
{
    // Don't persist across scenes
    protected override bool Preserve => false;
}
```
