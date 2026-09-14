using System;
using UnityEngine;

namespace Raven.Core
{
    public static class PhysicsQueries
    {
        // A full NonAlloc buffer means results may be missing. Grow only on saturation,
        // then reuse the larger buffer on subsequent queries.
        public static int OverlapSphere(Vector3 center, float radius, ref Collider[] results,
            int layerMask = Physics.AllLayers, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        {
            if (results == null || results.Length == 0) results = new Collider[8];
            while (true)
            {
                int count = Physics.OverlapSphereNonAlloc(center, radius, results, layerMask, triggers);
                if (count < results.Length) return count;
                Array.Resize(ref results, checked(results.Length * 2));
            }
        }

        public static int Raycast(Vector3 origin, Vector3 direction, ref RaycastHit[] results, float distance,
            int layerMask = Physics.DefaultRaycastLayers, QueryTriggerInteraction triggers = QueryTriggerInteraction.UseGlobal)
        {
            if (results == null || results.Length == 0) results = new RaycastHit[8];
            while (true)
            {
                int count = Physics.RaycastNonAlloc(origin, direction, results, distance, layerMask, triggers);
                if (count < results.Length) return count;
                Array.Resize(ref results, checked(results.Length * 2));
            }
        }
    }
}
