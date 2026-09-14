using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;

namespace Raven.Manager
{
    public class CoroutinesManager : MonoBehaviour
    {
        private sealed class TrackedCoroutine
        {
            public Coroutine Coroutine;
            public object Publisher;
            public bool Completed;
        }

        private readonly Dictionary<object, HashSet<Coroutine>> coroutinesLookup = new Dictionary<object, HashSet<Coroutine>>();

        public Coroutine StartCoroutine(IEnumerator enumerator, object publisher)
        {
            if (enumerator == null) throw new ArgumentNullException(nameof(enumerator));
            if (publisher == null) throw new ArgumentNullException(nameof(publisher));
            var tracked = new TrackedCoroutine { Publisher = publisher };
            Coroutine coroutine = base.StartCoroutine(RunTracked(enumerator, tracked));
            tracked.Coroutine = coroutine;

            if (tracked.Completed)
            {
                return coroutine;
            }

            if (coroutinesLookup.TryGetValue(publisher, out HashSet<Coroutine> coroutines))
            {
                coroutines.Add(coroutine);
            }
            else
            {
                coroutinesLookup.Add(publisher, new HashSet<Coroutine>() { coroutine });
            }

            return coroutine;
        }

        private IEnumerator RunTracked(IEnumerator enumerator, TrackedCoroutine tracked)
        {
            try
            {
                while (enumerator.MoveNext())
                {
                    yield return enumerator.Current;
                }
            }
            finally
            {
                tracked.Completed = true;
                if (tracked.Coroutine != null)
                {
                    RemoveTracking(tracked.Publisher, tracked.Coroutine);
                }

                (enumerator as IDisposable)?.Dispose();
            }
        }

        public void StopCoroutine(Coroutine coroutine, object publisher)
        {
            if (coroutine == null) return;

            if (coroutinesLookup.TryGetValue(publisher, out HashSet<Coroutine> coroutines) && coroutines.Remove(coroutine))
            {
                base.StopCoroutine(coroutine);

                if (coroutines.Count == 0)
                {
                    coroutinesLookup.Remove(publisher);
                }
            }
        }

        public void StopAllCoroutines(object publisher)
        {
            if (coroutinesLookup.TryGetValue(publisher, out HashSet<Coroutine> coroutines))
            {
                // Detach before stopping: cleanup must not mutate the set being enumerated.
                coroutinesLookup.Remove(publisher);

                foreach (Coroutine coroutine in coroutines)
                {
                    base.StopCoroutine(coroutine);
                }
            }
        }

        public void DestroyPublisher(object publisher)
        {
            StopAllCoroutines(publisher);
        }

        private void OnDisable()
        {
            base.StopAllCoroutines();
            coroutinesLookup.Clear();
        }

        private void RemoveTracking(object publisher, Coroutine coroutine)
        {
            if (!coroutinesLookup.TryGetValue(publisher, out HashSet<Coroutine> coroutines))
            {
                return;
            }

            coroutines.Remove(coroutine);
            if (coroutines.Count == 0)
            {
                coroutinesLookup.Remove(publisher);
            }
        }
    }
}

