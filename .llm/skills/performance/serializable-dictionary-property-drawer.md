---
title: "Serializable Dictionary Property Drawer"
id: "serializable-dictionary-property-drawer"
category: "performance"
version: "1.0.0"
created: "2026-01-21"
updated: "2026-01-21"

source:
  repository: "wallstop/unity-helpers"
  files:
    - path: "Runtime/Core/DataStructure/SerializableDictionary.cs"
    - path: "Editor/SerializableDictionaryDrawer.cs"
  url: "https://github.com/wallstop/unity-helpers"

tags:
  - "unity"
  - "serialization"
  - "dictionary"
  - "inspector"

complexity:
  level: "intermediate"
  reasoning: "Extends dictionary serialization and editor tooling"

impact:
  performance:
    rating: "medium"
    details: "Editor tooling only; runtime cost unchanged"
  maintainability:
    rating: "medium"
    details: "Improves usability in the Inspector"
  testability:
    rating: "low"
    details: "Primarily editor behavior"

prerequisites:
  - "Understanding of Unity serialization"

dependencies:
  packages: []
  skills:
    - "serializable-dictionary"

applies_to:
  languages:
    - "C#"
  frameworks:
    - "Unity"
  versions:
    unity: ">=2021.3"

aliases:
  - "Serializable dictionary editor"

related:
  - "serializable-dictionary"

status: "stable"
---

# Serializable Dictionary Property Drawer

> **One-line summary**: Custom property drawer to present key/value pairs cleanly in the Inspector.

## Overview

This skill covers a custom Inspector drawer for serializable dictionaries.

## Solution

Implement the drawer below to improve dictionary editing in Unity.

### Custom Property Drawer (Optional)

```csharp
#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor
{
    using UnityEditor;
    using UnityEngine;

    [CustomPropertyDrawer(typeof(SerializableDictionary<,>), true)]
    public class SerializableDictionaryDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty keysProperty = property.FindPropertyRelative("keys");
            SerializedProperty valuesProperty = property.FindPropertyRelative("values");

            property.isExpanded = EditorGUI.Foldout(
                new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
                property.isExpanded,
                label,
                true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                float y = position.y + EditorGUIUtility.singleLineHeight + 2;

                for (int i = 0; i < keysProperty.arraySize; i++)
                {
                    Rect keyRect = new Rect(position.x, y, position.width * 0.45f, EditorGUIUtility.singleLineHeight);
                    Rect valueRect = new Rect(position.x + position.width * 0.5f, y, position.width * 0.45f, EditorGUIUtility.singleLineHeight);
                    Rect removeRect = new Rect(position.x + position.width * 0.96f, y, position.width * 0.04f, EditorGUIUtility.singleLineHeight);

                    EditorGUI.PropertyField(keyRect, keysProperty.GetArrayElementAtIndex(i), GUIContent.none);
                    EditorGUI.PropertyField(valueRect, valuesProperty.GetArrayElementAtIndex(i), GUIContent.none);

                    if (GUI.Button(removeRect, "-"))
                    {
                        keysProperty.DeleteArrayElementAtIndex(i);
                        valuesProperty.DeleteArrayElementAtIndex(i);
                    }

                    y += EditorGUIUtility.singleLineHeight + 2;
                }

                Rect addRect = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
                if (GUI.Button(addRect, "Add Entry"))
                {
                    keysProperty.arraySize++;
                    valuesProperty.arraySize++;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            SerializedProperty keysProperty = property.FindPropertyRelative("keys");
            int lineCount = keysProperty.arraySize + 2; // +2 for header and add button
            return lineCount * (EditorGUIUtility.singleLineHeight + 2);
        }
    }
}
#endif
```
