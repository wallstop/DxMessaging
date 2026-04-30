namespace DxMessaging.Editor.CustomEditors
{
#if UNITY_EDITOR
    using DxMessaging.Unity;
    using Unity;
    using UnityEditor;

    /// <summary>
    /// Fallback CustomEditor that renders the DxMessaging warning HelpBox above the default
    /// inspector for any <see cref="MessageAwareComponent"/> subclass. Registered with
    /// <c>isFallback: true</c> so a user's own <c>[CustomEditor]</c> still wins precedence — this
    /// only kicks in when no other editor is registered for the type.
    /// </summary>
    /// <remarks>
    /// This composes the <see cref="Editor.finishedDefaultHeaderGUI"/> overlay path; the two
    /// paths cover different Unity inspector code paths. Notably, Unity 2021 does not reliably
    /// fire <see cref="Editor.finishedDefaultHeaderGUI"/> for <see cref="UnityEngine.MonoBehaviour"/>
    /// subclasses that have no <c>[CustomEditor]</c> registered — the fallback editor is what
    /// makes the HelpBox appear in that environment. To avoid double-rendering when the header
    /// hook ALSO fires for our editor instance (Unity 2022+),
    /// <see cref="MessageAwareComponentInspectorOverlay"/> unconditionally skips the header path
    /// for <see cref="MessageAwareComponentFallbackEditor"/> instances.
    ///
    /// <para>
    /// We deliberately do NOT call <see cref="Editor.DrawDefaultInspector"/>: it re-emits the
    /// <c>m_Script</c> field that Unity has already drawn in the inspector titlebar/header,
    /// producing a duplicate "Script" row that visually breaks the inspector and offsets the
    /// layout cache. Instead we walk the <see cref="SerializedObject"/> manually and skip
    /// <c>m_Script</c> — the canonical "default inspector minus the script field" pattern.
    /// </para>
    ///
    /// <para>
    /// We also do NOT short-circuit <see cref="OnInspectorGUI"/> on event type. Unity invokes
    /// editors twice per frame (Layout + Repaint), and both passes MUST emit identical control
    /// counts, otherwise the inspector window's layout cache is corrupted and adjacent
    /// components fail to render. See
    /// <see cref="MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI"/> for the
    /// matching invariant on the overlay side.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(MessageAwareComponent), editorForChildClasses: true)]
    [CanEditMultipleObjects]
    public sealed class MessageAwareComponentFallbackEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Render the overlay BEFORE the default body so the warning appears prominently at
            // the top of the inspector. The overlay's render body has identical Layout/Repaint
            // control counts, so we can call it unconditionally here.
            MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI(target);

            serializedObject.Update();
            SerializedProperty iter = serializedObject.GetIterator();
            if (iter.NextVisible(enterChildren: true))
            {
                do
                {
                    // Skip the script reference — Unity's inspector window already draws it in
                    // the component header. Re-drawing it here causes a duplicate "Script" row
                    // that visually breaks the inspector and offsets the layout cache.
                    if (
                        string.Equals(
                            iter.propertyPath,
                            "m_Script",
                            System.StringComparison.Ordinal
                        )
                    )
                    {
                        continue;
                    }
                    EditorGUILayout.PropertyField(iter, includeChildren: true);
                } while (iter.NextVisible(enterChildren: false));
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
#endif
}
