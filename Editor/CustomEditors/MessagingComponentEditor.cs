namespace DxMessaging.Editor.CustomEditors
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using System.Linq;
    using Core;
    using Core.Diagnostics;
    using Unity;
    using UnityEditor;
    using UnityEngine;

    [CustomEditor(typeof(MessagingComponent))]
    public sealed class MessagingComponentEditor : Editor
    {
        private readonly Dictionary<MonoBehaviour, bool> _listenerFoldouts = new();
        private readonly Dictionary<MonoBehaviour, int> _listenerPaging = new();
        private const int PageSize = 5;

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            MessagingComponent component = target as MessagingComponent;
            if (component == null)
            {
                return;
            }

            if (component._registeredListeners.Count == 0)
            {
                EditorGUILayout.LabelField("No listeners registered.");
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Global Diagnostics", EditorStyles.boldLabel);

            using (new GUILayout.HorizontalScope())
            {
                if (
                    GUILayout.Button(
                        $"Enable Diagnostics for All ({component._registeredListeners.Count})"
                    )
                )
                {
                    foreach (
                        MessageRegistrationToken token in component._registeredListeners.Values
                    )
                    {
                        token.DiagnosticMode = true;
                    }
                }

                if (
                    GUILayout.Button(
                        $"Disable Diagnostics for All ({component._registeredListeners.Count})"
                    )
                )
                {
                    foreach (
                        MessageRegistrationToken token in component._registeredListeners.Values
                    )
                    {
                        token.DiagnosticMode = false;
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Listeners", EditorStyles.boldLabel);

            List<MonoBehaviour> listeners = new(component._registeredListeners.Keys);

            foreach (MonoBehaviour listener in listeners)
            {
                if (listener == null)
                {
                    continue;
                }

                if (
                    !component._registeredListeners.TryGetValue(
                        listener,
                        out MessageRegistrationToken token
                    )
                )
                {
                    continue;
                }

                _listenerFoldouts.TryAdd(listener, false);
                _listenerFoldouts[listener] = EditorGUILayout.Foldout(
                    _listenerFoldouts[listener],
                    listener.GetType().Name,
                    true
                );
                if (_listenerFoldouts[listener])
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.ObjectField("Listener", listener, typeof(MonoBehaviour), true);
                    token.DiagnosticMode = EditorGUILayout.Toggle(
                        "Enable Diagnostics",
                        token.DiagnosticMode
                    );
                    if (token.DiagnosticMode)
                    {
                        if (token._metadata.Count == 0)
                        {
                            EditorGUILayout.LabelField("No messages registered for this listener.");
                        }
                        else
                        {
                            DrawPaginatedRegistrations(listener, token);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "Enable diagnostics to view registration details.",
                            MessageType.Info
                        );
                    }

                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawPaginatedRegistrations(
            MonoBehaviour listener,
            MessageRegistrationToken token
        )
        {
            _listenerPaging.TryAdd(listener, 0);
            int page = _listenerPaging[listener];
            int totalRegistrations = token._metadata.Count;
            int totalPages = (totalRegistrations + PageSize - 1) / PageSize;
            page = Mathf.Clamp(page, 0, totalPages - 1);

            EditorGUILayout.LabelField("Registrations", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            if (totalPages > 1)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = page > 0;
                    if (GUILayout.Button("<< Previous"))
                    {
                        _listenerPaging[listener]--;
                    }
                    GUI.enabled = true;

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField($"Page {page + 1} of {totalPages}");
                    GUILayout.FlexibleSpace();

                    GUI.enabled = page < totalPages - 1;
                    if (GUILayout.Button("Next >>"))
                    {
                        _listenerPaging[listener]++;
                    }
                    GUI.enabled = true;
                }
            }

            IEnumerable<
                KeyValuePair<MessageRegistrationHandle, MessageRegistrationMetadata>
            > pagedRegistrations = token
                ._metadata.OrderBy(kvp => kvp.Key)
                .Skip(page * PageSize)
                .Take(PageSize);

            foreach (
                (
                    MessageRegistrationHandle handle,
                    MessageRegistrationMetadata metadata
                ) in pagedRegistrations
            )
            {
                int callCount = token._callCounts.GetValueOrDefault(handle, 0);

                string messageName = metadata.type?.Name ?? string.Empty;
                EditorGUILayout.LabelField(messageName, EditorStyles.boldLabel);

                EditorGUI.indentLevel++;

                EditorGUILayout.LabelField("Type", metadata.registrationType.ToString());
                EditorGUILayout.LabelField("Priority", metadata.priority.ToString());
                EditorGUILayout.LabelField("Call Count", callCount.ToString());
                if (metadata.context?.Object != null)
                {
                    Object unityObject = metadata.context.Value.Object;
                    EditorGUILayout.LabelField(
                        "Context",
                        $"{unityObject.name} - {unityObject.GetType().Name}"
                    );
                }

                EditorGUI.indentLevel--;
                EditorGUILayout.Space();
            }
            EditorGUI.indentLevel--;
        }
    }
#endif
}
