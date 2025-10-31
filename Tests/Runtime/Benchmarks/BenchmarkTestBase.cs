#if UNITY_2021_3_OR_NEWER
namespace DxMessaging.Tests.Runtime.Benchmarks
{
    using System;
    using System.Globalization;
    using DxMessaging.Tests.Runtime.Core;
    using Scripts.Components;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public abstract class BenchmarkTestBase : MessagingTestBase
    {
        protected const int NumInvocationsPerIteration = 10_000;

        private BenchmarkSession _session;

        protected BenchmarkSession CurrentSession => _session;

        protected void RunWithSession(BenchmarkSession session, Action body)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            _session = session;
            try
            {
                using (session)
                {
                    body();
                }
            }
            finally
            {
                _session = null;
            }
        }

        protected void RecordBenchmark(string label, int count, TimeSpan duration, bool allocating)
        {
            long operationsPerSecond =
                duration.TotalSeconds <= 0.0 ? 0 : (long)Math.Floor(count / duration.TotalSeconds);

            if (_session != null)
            {
                _session.Record(label, operationsPerSecond, allocating);
                return;
            }

            string formatted = operationsPerSecond.ToString("N0", CultureInfo.InvariantCulture);
            Debug.Log($"| {label} | {formatted} | {(allocating ? "Yes" : "No")} |");
        }

        protected GameObject CreateBenchmarkGameObject()
        {
            GameObject target = new(
                "Benchmark",
                typeof(EmptyMessageAwareComponent),
                typeof(SpriteRenderer),
                typeof(Rigidbody2D),
                typeof(CircleCollider2D),
                typeof(LineRenderer)
            );
            _spawned.Add(target);
            return target;
        }

        protected void RunWithComponent(Action<EmptyMessageAwareComponent> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            GameObject go = CreateBenchmarkGameObject();
            try
            {
                EmptyMessageAwareComponent component =
                    go.GetComponent<EmptyMessageAwareComponent>();
                action(component);
            }
            finally
            {
                _spawned.Remove(go);
                Object.Destroy(go);
            }
        }
    }
}

#endif
