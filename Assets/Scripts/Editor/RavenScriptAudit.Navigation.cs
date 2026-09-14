using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Raven.Config;
using Raven.Enemy;
using Raven.Manager;
using UnityEngine;
using UnityEngine.AI;
using Zenject;
using Object = UnityEngine.Object;

public static partial class RavenScriptAudit
{
    private static IEnumerator CheckNavigation(SceneContext context)
    {
        var fixture = new GameObject("Audit navigation fixture") { hideFlags = HideFlags.DontSave };
        var config = ScriptableObject.CreateInstance<EnemyConfig>();
        NavMeshData data = null;
        NavMeshDataInstance instance = default;
        NavMeshAgent agent = null;
        try
        {
            Check(NavMesh.GetSettingsCount() > 0, "Navigation fixture has an agent build type");
            if (NavMesh.GetSettingsCount() == 0) yield break;
            NavMeshBuildSettings settings = NavMesh.GetSettingsByIndex(0);
            var sources = new List<NavMeshBuildSource>
            {
                new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    size = new Vector3(64f, 1f, 64f),
                    transform = Matrix4x4.Translate(Vector3.down * 0.5f),
                    area = 0
                }
            };
            data = NavMeshBuilder.BuildNavMeshData(settings, sources,
                new Bounds(Vector3.zero, new Vector3(64f, 8f, 64f)), Vector3.zero, Quaternion.identity);
            Check(data != null, "Navigation fixture builds separate NavMeshData from a box");
            if (data == null) yield break;
            Vector3 origin = new Vector3(30000f, 1000f, 30000f);
            instance = NavMesh.AddNavMeshData(data, origin, Quaternion.identity);
            var filter = new NavMeshQueryFilter { agentTypeID = settings.agentTypeID, areaMask = NavMesh.AllAreas };
            NavMeshHit hit = default;
            bool found = instance.valid && NavMesh.SamplePosition(origin, out hit, 2f, filter);
            Check(found, "Isolated navigation surface is available far from the Level");
            if (!found) yield break;

            var enemy = new GameObject("Audit charging enemy");
            enemy.SetActive(false);
            enemy.transform.SetParent(fixture.transform);
            enemy.transform.position = hit.position;
            agent = enemy.AddComponent<NavMeshAgent>();
            agent.agentTypeID = settings.agentTypeID;
            agent.radius = settings.agentRadius;
            agent.height = settings.agentHeight;
            enemy.SetActive(true);
            var gfx = new GameObject("Audit GFX");
            gfx.transform.SetParent(enemy.transform, false);
            Vector3 offset = new Vector3(0.125f, 0.75f, 0.25f);
            gfx.transform.localPosition = offset;
            var player = new GameObject("Audit navigation target");
            player.transform.SetParent(fixture.transform);
            player.transform.position = hit.position + Vector3.forward * 12f;
            void Configure(string name, float value) => typeof(EnemyConfig)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(config, value);
            Configure("_moveSpeed", 2f);
            Configure("_chargeSpeedModifier", 4f);
            Configure("_chargeDistance", 20f);
            Configure("_chargeTime", 0.5f);
            Configure("_chargeWaitTime", 0.4f);
            CoroutinesManager coroutines = fixture.AddComponent<CoroutinesManager>();
            Random.State randomState = Random.state;
            Kamikaze behaviour;
            try { behaviour = new Kamikaze(config, player.transform, agent, gfx.transform, coroutines, null, enemy, null, null, null); }
            finally { Random.state = randomState; }
            yield return null;
            Vector3 start = enemy.transform.position;
            bool started = false, stayedOnMesh = true, keptOffset = true;
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                behaviour.Behaviour();
                started |= Field<bool>(behaviour, "_charging");
                stayedOnMesh &= agent.isOnNavMesh;
                keptOffset &= Vector3.Distance(gfx.transform.localPosition, offset) < 0.001f;
                if (!Field<bool>(behaviour, "_charge")) break;
                yield return null;
            }
            Vector3 displacement = enemy.transform.position - start;
            Check(started && stayedOnMesh && displacement.z > 1f && Mathf.Abs(displacement.y) < 0.1f,
                "Kamikaze charge moves the root forward with NavMeshAgent.Move and remains on its surface");
            Check(keptOffset, "Kamikaze charge preserves the GFX local offset throughout movement and completion");
            bool completed = !Field<bool>(behaviour, "_charging") && !Field<bool>(behaviour, "_charge");
            Check(completed && !agent.isStopped && Field<float>(behaviour, "_chargeTimer") == 0f,
                "Kamikaze finishes its charge, resumes navigation and enters cooldown");
            if (!completed) yield break;
            behaviour.Behaviour();
            Check(!Field<bool>(behaviour, "_charging") && !Field<bool>(behaviour, "_charge"),
                "Kamikaze cannot charge again during cooldown even with a target in range");
            yield return new WaitForSeconds(config.ChargeWaitTime + 0.1f);
            behaviour.Behaviour();
            Check(Field<bool>(behaviour, "_charging") && agent.isStopped && agent.isOnNavMesh,
                "Kamikaze can begin another charge after its real cooldown coroutine completes");
        }
        finally
        {
            if (agent != null) agent.enabled = false;
            fixture.SetActive(false);
            if (instance.valid) instance.Remove();
            if (data != null) Object.Destroy(data);
            Object.Destroy(config);
            Object.Destroy(fixture);
        }
        yield return null;
    }
}
