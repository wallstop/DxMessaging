#if UNITY_EDITOR && UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using DxMessaging.Editor.CustomEditors;
    using DxMessaging.Editor.Settings;
    using DxMessaging.Unity;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class MessageAwareComponentFallbackEditorTests
    {
        public enum OverlayTargetScenario
        {
            NullObject,
            GameObject,
            Transform,
        }

        private readonly List<Object> _createdObjects = new();
        private readonly List<UnityEditor.Editor> _createdEditors = new();
        private bool _previousBaseCallCheckEnabled;
        private bool _baseCallCheckOverridden;

        [SetUp]
        public void SetUp()
        {
            // Disable diagnostic noise so the overlay's BuildAndRenderOverlay returns early via
            // the gating phase (no EditorGUILayout calls). Stale entries from a previous session
            // could otherwise drive the body into shape != 0 and pollute the body assertion.
            //
            // We reset the override flag BEFORE the throwing call so that if GetOrCreateSettings
            // throws, TearDown sees no override and skips restoration. We capture the previous
            // value into the field BEFORE marking the override as active, so a throw on the
            // capture or the subsequent write still leaves TearDown with the correct
            // captured-vs-overridden state.
            _baseCallCheckOverridden = false;
            DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
            _previousBaseCallCheckEnabled = settings._baseCallCheckEnabled;
            _baseCallCheckOverridden = true;
            settings._baseCallCheckEnabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEditor.Editor editor in _createdEditors)
            {
                if (editor != null)
                {
                    Object.DestroyImmediate(editor);
                }
            }
            _createdEditors.Clear();

            foreach (Object instance in _createdObjects)
            {
                if (instance != null)
                {
                    Object.DestroyImmediate(instance);
                }
            }
            _createdObjects.Clear();

            if (_baseCallCheckOverridden)
            {
                DxMessagingSettings settings = DxMessagingSettings.GetOrCreateSettings();
                settings._baseCallCheckEnabled = _previousBaseCallCheckEnabled;
                _baseCallCheckOverridden = false;
            }
        }

        [Test]
        public void FallbackEditorMustRegisterAsPrimaryNonFallbackEditorForChildClasses()
        {
            // The [CustomEditor] attribute MUST register this editor as a PRIMARY (non-fallback)
            // editor for every MessageAwareComponent subclass. Earlier attempts to use
            // isFallback = true caused Unity to skip our editor entirely and pick GenericInspector
            // instead; which dropped the missing-base-call HelpBox warnings on every component
            // because Unity 2021's Editor.finishedDefaultHeaderGUI hook does not reliably fire for
            // MonoBehaviour subclasses that have no registered [CustomEditor].
            //
            // The "empty vertical gap below the header" bug that motivated the isFallback attempt
            // is solved orthogonally: OnInspectorGUI calls Editor.DrawDefaultInspector(), so the
            // body matches Unity's GenericInspector exactly (including the disabled "Script" row
            // every MonoBehaviour shows). There is no missing row to leave a gap.
            //
            // CustomEditor.isFallback has been a public field on UnityEditor.CustomEditor since
            // at least Unity 2017.2; we read it directly without reflection. The contract:
            // isFallback MUST be false (default), editorForChildClasses MUST be true.
            Type fallbackType = typeof(MessageAwareComponentFallbackEditor);
            object[] attributes = fallbackType.GetCustomAttributes(
                typeof(CustomEditor),
                inherit: false
            );
            Assert.That(
                attributes.Length,
                Is.EqualTo(1),
                "MessageAwareComponentFallbackEditor must declare exactly one [CustomEditor] attribute."
            );

            CustomEditor customEditor = (CustomEditor)attributes[0];
            Assert.That(
                customEditor.isFallback,
                Is.False,
                "MessageAwareComponentFallbackEditor must register with isFallback = false (the default). Setting isFallback = true causes Unity to prefer GenericInspector for every MessageAwareComponent subclass, which silently drops the missing-base-call HelpBox warnings; the regression this test was added to prevent."
            );

            FieldInfo editorForChildClassesField = typeof(CustomEditor).GetField(
                "m_EditorForChildClasses",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            Assert.That(
                editorForChildClassesField,
                Is.Not.Null,
                "Unity's CustomEditor.m_EditorForChildClasses field is missing; Unity may have renamed the field; update this test."
            );
            bool editorForChildClasses = (bool)editorForChildClassesField.GetValue(customEditor);
            Assert.That(
                editorForChildClasses,
                Is.True,
                "MessageAwareComponentFallbackEditor must register with editorForChildClasses: true so that ALL MessageAwareComponent subclasses get the warning HelpBox."
            );
        }

        [Test]
        public void FallbackEditorIsSelectedForSubclassWithoutCustomEditor()
        {
            // End-to-end check: Unity must select our editor for MessageAwareComponent
            // subclasses that have no user-defined [CustomEditor]. With isFallback = false and
            // editorForChildClasses = true, our editor is the most-specific match for any
            // MessageAwareComponent subclass that has no dedicated user editor.
            GameObject host = CreateTrackedObject("FallbackEditorSelectionHost");
            EmptyMessageAwareComponentForFallbackTest component =
                host.AddComponent<EmptyMessageAwareComponentForFallbackTest>();
            Assert.That(component, Is.Not.Null, "Failed to attach test subclass to host.");

            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(component);
            _createdEditors.Add(editor);

            Assert.That(
                editor,
                Is.Not.Null,
                "Editor.CreateEditor returned null for the empty subclass; Unity could not resolve any editor."
            );
            Assert.That(
                editor,
                Is.InstanceOf<MessageAwareComponentFallbackEditor>(),
                "Unity must select MessageAwareComponentFallbackEditor for a MessageAwareComponent subclass with no user-defined [CustomEditor]."
            );
        }

        [TestCase(typeof(EmptyMessageAwareComponentForFallbackTest))]
        [TestCase(typeof(SerializedFieldMessageAwareComponentForFallbackTest))]
        public void OverlayDoesNotRenderWhenBaseCallCheckIsDisabled(Type componentType)
        {
            // This test intentionally avoids calling Editor.OnInspectorGUI directly: invoking
            // DrawDefaultInspector() outside Unity's active IMGUI cycle throws inside
            // GUILayoutUtility. Instead we assert the overlay body itself short-circuits with
            // shape == 0 (returns false, emits no UI) when the base-call check is disabled.
            MessageAwareComponent component = CreateTrackedMessageAwareComponent(
                $"FallbackEditorBodyHost_{componentType.Name}",
                componentType
            );

            bool rendered = InvokeBuildAndRenderOverlay(component);
            Assert.That(
                rendered,
                Is.False,
                "BuildAndRenderOverlay must return false (render nothing) when base-call checks are disabled. "
                    + $"ComponentType={componentType.FullName}; GUI context: {DescribeCurrentGuiContext()}."
            );

            Assert.DoesNotThrow(
                () => MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI(component),
                "RenderInsideOnInspectorGUI must remain a no-op when overlay rendering is gated off. "
                    + $"ComponentType={componentType.FullName}; GUI context: {DescribeCurrentGuiContext()}."
            );
        }

        [TestCase(OverlayTargetScenario.NullObject)]
        [TestCase(OverlayTargetScenario.GameObject)]
        [TestCase(OverlayTargetScenario.Transform)]
        public void RenderInsideOnInspectorGUIGracefullyNoOpsForUnsupportedTargets(
            OverlayTargetScenario scenario
        )
        {
            Object target = CreateTargetForScenario(scenario);

            Assert.DoesNotThrow(
                () => MessageAwareComponentInspectorOverlay.RenderInsideOnInspectorGUI(target),
                "RenderInsideOnInspectorGUI must no-op for unsupported targets rather than throwing. "
                    + $"Scenario={scenario}; GUI context: {DescribeCurrentGuiContext()}."
            );
        }

        private MessageAwareComponent CreateTrackedMessageAwareComponent(string hostName, Type type)
        {
            Assert.That(type, Is.Not.Null, "Component type test input must not be null.");
            Assert.That(
                typeof(MessageAwareComponent).IsAssignableFrom(type),
                Is.True,
                $"Test input type must derive from {nameof(MessageAwareComponent)}. Actual: {type.FullName}."
            );

            GameObject host = CreateTrackedObject(hostName);
            Component component = host.AddComponent(type);
            Assert.That(
                component,
                Is.Not.Null,
                $"Failed to attach {type.FullName} to host GameObject."
            );

            MessageAwareComponent messageAwareComponent = component as MessageAwareComponent;
            Assert.That(
                messageAwareComponent,
                Is.Not.Null,
                $"Attached component must be assignable to {nameof(MessageAwareComponent)}. Actual: {component.GetType().FullName}."
            );
            return messageAwareComponent;
        }

        private static bool InvokeBuildAndRenderOverlay(MessageAwareComponent component)
        {
            Assert.That(component, Is.Not.Null, "Component under test must not be null.");

            MethodInfo method = typeof(MessageAwareComponentInspectorOverlay).GetMethod(
                "BuildAndRenderOverlay",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            Assert.That(
                method,
                Is.Not.Null,
                "MessageAwareComponentInspectorOverlay.BuildAndRenderOverlay was not found. "
                    + "If this method was renamed, update this test helper."
            );

            try
            {
                object result = method.Invoke(null, new object[] { component });
                Assert.That(
                    result,
                    Is.TypeOf<bool>(),
                    "BuildAndRenderOverlay must return bool so callers can reason about whether overlay UI was emitted."
                );
                return (bool)result;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Assert.Fail(
                    "BuildAndRenderOverlay threw unexpectedly while base-call checks were disabled. "
                        + $"Inner exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}. "
                        + $"GUI context: {DescribeCurrentGuiContext()}."
                );
                return false;
            }
        }

        private static string DescribeCurrentGuiContext()
        {
            Event currentEvent = Event.current;
            if (currentEvent == null)
            {
                return "Event.current=<null>";
            }
            return $"Event.current.type={currentEvent.type}";
        }

        private Object CreateTargetForScenario(OverlayTargetScenario scenario)
        {
            switch (scenario)
            {
                case OverlayTargetScenario.NullObject:
                    return null;
                case OverlayTargetScenario.GameObject:
                    return CreateTrackedObject("OverlayTargetGameObject");
                case OverlayTargetScenario.Transform:
                    return CreateTrackedObject("OverlayTargetTransform").transform;
                default:
                    Assert.Fail($"Unhandled {nameof(OverlayTargetScenario)} value: {scenario}.");
                    return null;
            }
        }

        private GameObject CreateTrackedObject(string name)
        {
            GameObject gameObject = new(name);
            _createdObjects.Add(gameObject);
            return gameObject;
        }
    }

    // Helper subclass used by the editor-selection / body-emission tests. Marked internal
    // because Unity cannot serialize private nested MonoBehaviours during domain reload, and
    // [AddComponentMenu("")] hides it from the inspector's Add Component picker.
    [AddComponentMenu("")]
    internal sealed class EmptyMessageAwareComponentForFallbackTest : MessageAwareComponent { }

    [AddComponentMenu("")]
    internal sealed class SerializedFieldMessageAwareComponentForFallbackTest
        : MessageAwareComponent
    {
        [SerializeField]
        private int _value;
    }
}
#endif
