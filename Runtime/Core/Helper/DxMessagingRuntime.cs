namespace DxMessaging.Core.Helper
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using Core;
    using Messages;
#if UNITY_2017_1_OR_NEWER
    using UnityEngine;
#endif
    public static class DxMessagingRuntime
    {
        public static int TotalMessageTypes { get; private set; }

        public static bool Initialized
        {
            get
            {
                lock (InitializationLock)
                {
                    return _isInitialized;
                }
            }
        }

        private static bool _isInitialized;
        private static readonly object InitializationLock = new();

        static DxMessagingRuntime()
        {
            Initialize();
        }

#if UNITY_2017_1_OR_NEWER
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
                        try
                        {
                            if (type == null)
                            {
                                continue;
                            }

                            if (!typeof(IMessage).IsAssignableFrom(type))
                            {
                                continue;
                            }

                            if (type.IsGenericTypeDefinition)
                            {
                                continue;
                            }

                            if (uniqueTypes.Add(type))
                            {
                                messageTypes.Add(type);
                            }
                        }
                        catch (Exception e)
                        {
                            Log(
                                () =>
                                    $"Error checking if {type?.FullName} is assignable from IMessage: {e}",
                                isError: true
                            );
                        }
                    }
                }

                if (uniqueTypes.Add(typeof(IMessage)))
                {
                    messageTypes.Add(typeof(IMessage));
                }
                if (uniqueTypes.Add(typeof(ITargetedMessage)))
                {
                    messageTypes.Add(typeof(ITargetedMessage));
                }
                if (uniqueTypes.Add(typeof(IBroadcastMessage)))
                {
                    messageTypes.Add(typeof(IBroadcastMessage));
                }
                if (uniqueTypes.Add(typeof(IUntargetedMessage)))
                {
                    messageTypes.Add(typeof(IUntargetedMessage));
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
                                    $"Error: Could not find field for SequentialId on MessageHelperIndexer<{messageType.FullName}>.",
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
#if UNITY_2017_1_OR_NEWER
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
            catch (Exception e)
            {
                string errorMessage = $"Error logging message: {e}";
#if UNITY_2017_1_OR_NEWER
                Debug.LogError(errorMessage);
#else
                Console.WriteLine(errorMessage);
#endif
            }
        }
    }
}
