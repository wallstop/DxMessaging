namespace DxMessaging.Editor.CustomEditors
{
#if UNITY_EDITOR
    using DxMessaging.Unity;
    using UnityEditor;

    /// <summary>
    /// Primary CustomEditor for every <see cref="MessageAwareComponent"/> subclass. Renders the
    /// DxMessaging warning HelpBox above an inspector body that is byte-for-byte identical to
    /// Unity's default <c>GenericInspector</c> (achieved via
    /// <see cref="Editor.DrawDefaultInspector"/>).
    /// </summary>
    /// <remarks>
    /// We register as a non-fallback (primary) editor with
    /// <c>editorForChildClasses: true</c>. Several alternatives were tried and rejected:
    ///
    /// <list type="number">
    /// <item>
    /// <b><c>isFallback = true</c>:</b> Unity selects this editor only when no other matches.
    /// In practice that meant Unity's <c>GenericInspector</c> handled every
    /// <see cref="MessageAwareComponent"/> subclass and the warning HelpBox vanished entirely
    /// (Unity 2021's <see cref="Editor.finishedDefaultHeaderGUI"/> hook did not reliably fire
    /// for those types). This regressed the analyzer warning surface.
    /// </item>
    /// <item>
    /// <b>Manual <see cref="SerializedObject"/> iteration that skips <c>m_Script</c>:</b> the
    /// rationale was to avoid a "duplicate Script row," but Unity does NOT draw <c>m_Script</c>
    /// in the component header -- <see cref="Editor.DrawDefaultInspector"/> draws the same
    /// disabled "Script" row that <c>GenericInspector</c> draws. Skipping it produced a visible
    /// vertical gap below the header for empty subclasses, because the row Unity reserves for
    /// the script reference was left blank.
    /// </item>
    /// </list>
    ///
    /// <para>
    /// The current design is the simple one: be the primary editor, prepend the overlay's
    /// HelpBox via <see cref="MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI"/>,
    /// then call <see cref="Editor.DrawDefaultInspector"/>. The body therefore matches
    /// <c>GenericInspector</c> exactly: no missing Script row, no extra vertical gap. To avoid
    /// double-rendering when the header hook ALSO fires for our editor instance (Unity 2022+),
    /// <see cref="MessageAwareComponentInspectorOverlay"/> unconditionally skips the header path
    /// for <see cref="MessageAwareComponentFallbackEditor"/> instances.
    /// </para>
    ///
    /// <para>
    /// We do NOT short-circuit <see cref="OnInspectorGUI"/> on event type. Unity invokes
    /// editors twice per frame (Layout + Repaint), and both passes MUST emit identical control
    /// counts, otherwise the inspector window's layout cache is corrupted and adjacent
    /// components fail to render. See
    /// <see cref="MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI"/> for the
    /// matching invariant on the overlay side.
    /// </para>
    ///
    /// <para>
    /// User-defined custom editors for specific <see cref="MessageAwareComponent"/> subclasses
    /// still win precedence: a <c>[CustomEditor(typeof(MySpecificSubclass))]</c> is more
    /// specific than our <c>editorForChildClasses</c> registration, so Unity selects the user's
    /// editor for that subclass. The header-hook overlay still surfaces the warning above the
    /// user's editor in that case.
    /// </para>
    /// </remarks>
    [CustomEditor(typeof(MessageAwareComponent), true)]
    [CanEditMultipleObjects]
    public sealed class MessageAwareComponentFallbackEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // Render the overlay BEFORE the default body so the warning appears prominently at
            // the top of the inspector. The overlay's render body has identical Layout/Repaint
            // control counts, so we can call it unconditionally here.
            MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI(target);

            // Match Unity's GenericInspector exactly; including the disabled "Script" row that
            // every MonoBehaviour inspector shows. This is intentional: skipping the script row
            // creates a visible empty gap below the header for subclasses with no
            // [SerializeField] fields.
            DrawDefaultInspector();
        }
    }
#endif
}
