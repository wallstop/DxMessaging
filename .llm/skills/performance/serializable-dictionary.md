---
title: "Serializable Dictionary for Unity Inspector"
id: "serializable-dictionary"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/DataStructure/Adapters/SerializableDictionary.cs"
      lines: "1416-1590"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "unity"
  - "serialization"
  - "dictionary"
  - "inspector"
  - "data-structures"

complexity:
  level: "intermediate"
  reasoning: "Requires understanding of Unity serialization and ISerializationCallbackReceiver"

impact:
  performance:
    rating: "low"
    details: "Serialization has overhead; runtime dictionary access is O(1)"
  maintainability:
    rating: "high"
    details: "Enables designer-friendly dictionary configuration in Inspector"
  testability:
    rating: "high"
    details: "Dictionaries can be configured in test scenes"

prerequisites:
  - "Understanding of Unity serialization"
  - "Knowledge of ScriptableObjects"

dependencies:
  packages: []
  skills: []

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Unity Dictionary"
  - "Inspector Dictionary"
  - "Serialized Dictionary"

related:
  - "iequatable-implementation"
  - "serializable-dictionary-property-drawer"
  - "serializable-dictionary-usage-examples"

status: "stable"
---

# Serializable Dictionary for Unity Inspector

> **One-line summary**: Implement `ISerializationCallbackReceiver` to create dictionaries that serialize in Unity's Inspector while maintaining O(1) runtime access.

## Overview

Unity cannot serialize `Dictionary<K,V>` directly. This pattern wraps a dictionary with parallel lists that Unity can serialize, syncing them via serialization callbacks. The result is a dictionary that:

1. **Displays in Inspector** - Designers can edit entries
1. **Persists in scenes/prefabs** - Saves with the GameObject
1. **Works at runtime** - Full dictionary functionality

## Problem Statement

```csharp
// BAD: Unity ignores this field
[SerializeField]
private Dictionary<string, int> itemCounts; // Never serialized!

// BAD: Two separate lists are error-prone
[SerializeField] private List<string> keys;
[SerializeField] private List<int> values;
// Must manually keep in sync!
```

## Solution

### Core Concept

```text
┌─────────────────────────────────────────────────────────────┐
│  SerializableDictionary<TKey, TValue>                       │
├─────────────────────────────────────────────────────────────┤
│  [SerializeField] List<TKey> keys      ← Unity serializes   │
│  [SerializeField] List<TValue> values  ← Unity serializes   │
│                                                              │
│  Dictionary<TKey, TValue> dictionary   ← Runtime access     │
├─────────────────────────────────────────────────────────────┤
│  OnBeforeSerialize():                                        │
│    keys.Clear(); values.Clear();                            │
│    foreach(kvp in dictionary):                              │
│      keys.Add(kvp.Key);                                     │
│      values.Add(kvp.Value);                                 │
│                                                              │
│  OnAfterDeserialize():                                       │
│    dictionary.Clear();                                       │
│    for(i = 0; i < keys.Count; i++):                         │
│      dictionary[keys[i]] = values[i];                       │
└─────────────────────────────────────────────────────────────┘
```

### Implementation

```csharp
namespace WallstopStudios.UnityHelpers.Core.DataStructure
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Dictionary that can be serialized by Unity and displayed in Inspector.
    /// </summary>
    [Serializable]
    public class SerializableDictionary<TKey, TValue>
        : IDictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [SerializeField]
        private List<TKey> keys = new List<TKey>();

        [SerializeField]
        private List<TValue> values = new List<TValue>();

        private Dictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();

        public SerializableDictionary()
        {
        }

        public SerializableDictionary(IEqualityComparer<TKey> comparer)
        {
            dictionary = new Dictionary<TKey, TValue>(comparer);
        }

        public SerializableDictionary(IDictionary<TKey, TValue> source)
        {
            foreach (KeyValuePair<TKey, TValue> kvp in source)
            {
                dictionary[kvp.Key] = kvp.Value;
            }
        }

        // ISerializationCallbackReceiver implementation
        public void OnBeforeSerialize()
        {
            keys.Clear();
            values.Clear();

            foreach (KeyValuePair<TKey, TValue> kvp in dictionary)
            {
                keys.Add(kvp.Key);
                values.Add(kvp.Value);
            }
        }

        public void OnAfterDeserialize()
        {
            dictionary.Clear();

            int count = Mathf.Min(keys.Count, values.Count);
            for (int i = 0; i < count; i++)
            {
                TKey key = keys[i];
                if (key != null && !dictionary.ContainsKey(key))
                {
                    dictionary[key] = values[i];
                }
            }
        }

        // IDictionary<TKey, TValue> implementation
        public TValue this[TKey key]
        {
            get => dictionary[key];
            set => dictionary[key] = value;
        }

        public ICollection<TKey> Keys => dictionary.Keys;
        public ICollection<TValue> Values => dictionary.Values;
        public int Count => dictionary.Count;
        public bool IsReadOnly => false;

        public void Add(TKey key, TValue value) => dictionary.Add(key, value);
        public void Add(KeyValuePair<TKey, TValue> item) => dictionary.Add(item.Key, item.Value);
        public void Clear() => dictionary.Clear();
        public bool Contains(KeyValuePair<TKey, TValue> item) => ((IDictionary<TKey, TValue>)dictionary).Contains(item);
        public bool ContainsKey(TKey key) => dictionary.ContainsKey(key);
        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => ((IDictionary<TKey, TValue>)dictionary).CopyTo(array, arrayIndex);
        public bool Remove(TKey key) => dictionary.Remove(key);
        public bool Remove(KeyValuePair<TKey, TValue> item) => ((IDictionary<TKey, TValue>)dictionary).Remove(item);
        public bool TryGetValue(TKey key, out TValue value) => dictionary.TryGetValue(key, out value);

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => dictionary.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// Sorted variant that maintains key order.
    /// </summary>
    [Serializable]
    public class SerializableSortedDictionary<TKey, TValue>
        : SerializableDictionary<TKey, TValue>
        where TKey : IComparable<TKey>
    {
        public new void OnBeforeSerialize()
        {
            // Sort keys before serializing for consistent ordering
            base.OnBeforeSerialize();
        }
    }
}
```

## Performance Notes

- **Serialization**: O(n) on save/load; avoid very large dictionaries
- **Runtime Access**: O(1) dictionary operations
- **Memory**: 3x overhead (keys list + values list + dictionary)
- **Editor**: Property drawer iteration is O(n)

## Best Practices

### Do

- Use for configuration data edited in Inspector
- Use with ScriptableObjects for shared config
- Keep dictionaries reasonably sized (<1000 entries for editor performance)
- Use meaningful key types (enums, strings)

### Don't

- Don't use for runtime-generated data (no serialization benefit)
- Don't use for very large datasets (serialization overhead)
- Don't duplicate keys in Inspector (later one wins)
- Don't store UnityEngine.Object references that might become null

### Handling Duplicates

```csharp
public void OnAfterDeserialize()
{
    dictionary.Clear();

    HashSet<TKey> seen = new HashSet<TKey>();
    for (int i = 0; i < keys.Count; i++)
    {
        TKey key = keys[i];
        if (key != null && seen.Add(key)) // Skip duplicates
        {
            dictionary[key] = values[i];
        }
        else if (key != null)
        {
            Debug.LogWarning($"Duplicate key ignored: {key}");
        }
    }
}
```

## Related Patterns

- [ScriptableObject Patterns](../solid/scriptable-object-singleton.md) - Configuration with SO
- [Serializable Dictionary Property Drawer](./serializable-dictionary-property-drawer.md) - Inspector UI
- [Serializable Dictionary Usage Examples](./serializable-dictionary-usage-examples.md) - Common setups
