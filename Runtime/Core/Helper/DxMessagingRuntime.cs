namespace DxMessaging.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Core;
#if UNITY_EDITOR
    using UnityEngine;
#endif
    public static class DxMessagingRuntime
    {
        public static int TotalMessageTypes { get; private set; }

        private static bool _isInitialized;
        private static readonly object InitializationLock = new();

        static DxMessagingRuntime()
        {
            Initialize();
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#endif
        public static void Initialize()
        {
            lock (InitializationLock)
            {
                if (_isInitialized)
                {
                    return;
                }

                Log(() => "DxMessagingRuntime Initializing...", isError: false);

                HashSet<Type> uniqueTypes = new();
                List<Type> messageTypes = new();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (Assembly assembly in assemblies)
                {
                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (Type type in types)
                    {
                        if (type == null)
                        {
                            continue;
                        }

                        if (
                            !typeof(IMessage).IsAssignableFrom(type)
                            || type.IsInterface
                            || type.IsAbstract
                        )
                        {
                            continue;
                        }

                        if (uniqueTypes.Add(type))
                        {
                            messageTypes.Add(type);
                        }
                    }
                }

                messageTypes.Sort(
                    (a, b) =>
                        string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase)
                );

                TotalMessageTypes = messageTypes.Count;
                Type helperIndexerGenericDef = typeof(MessageHelperIndexer<>);

                for (int i = 0; i < TotalMessageTypes; ++i)
                {
                    Type messageType = messageTypes[i];
                    try
                    {
                        Type specificHelperType = helperIndexerGenericDef.MakeGenericType(
                            messageType
                        );

                        FieldInfo idField = specificHelperType.GetField(
                            "SequentialId",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
                        );
                        if (idField != null)
                        {
                            idField.SetValue(null, i);
                        }
                        else
                        {
                            Log(
                                () =>
                                    $"Error: Could not find field for SequentialId on MessageHelperIndexer<{messageType.Name}>.",
                                isError: true
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(
                            () => $"Error setting SequentialId for {messageType.FullName}: {ex}",
                            isError: true
                        );
                    }
                }

                _isInitialized = true;
                Log(
                    () =>
                        $"DxMessagingRuntime Initialized. Found {TotalMessageTypes} message types.",
                    isError: false
                );
            }
        }

        private static void Log(Func<string> messageProducer, bool isError)
        {
            try
            {
                string message = messageProducer();
#if UNITY_EDITOR

                if (isError)
                {
                    Debug.LogError(message);
                }
                else
                {
                    Debug.Log(message);
                }
#else
                Console.WriteLine(message);
#endif
            }
            catch
            {
                // Swallow
            }
        }
    }
}
