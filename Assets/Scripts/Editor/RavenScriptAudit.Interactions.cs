using System.Collections;
using System.Reflection;
using Raven.Config;
using Raven.Enemy;
using Raven.Input;
using Raven.Manager;
using Raven.Player;
using UnityEngine;
using UnityEngine.AI;
using Zenject;
using Object = UnityEngine.Object;
using PuzzleActivator = Raven.Puzzle.Activator;

public static partial class RavenScriptAudit
{
    private static IEnumerator CheckInteractions(SceneContext context)
    {
        var fixture = new GameObject("Audit interactions fixture") { hideFlags = HideFlags.DontSave };
        var enemyConfig = ScriptableObject.CreateInstance<EnemyConfig>();
        var movementConfig = ScriptableObject.CreateInstance<MovementConfig>();
        CameraManager camera = null;
        PlayerMovementManager movement = null;
        Vector3 origin = new Vector3(20000f, 20000f, 20000f);
        try
        {
            var player = new GameObject("Audit player");
            player.transform.SetParent(fixture.transform);
            player.transform.position = origin + Vector3.right * 100f;
            player.tag = "Player";
            CharacterController playerCollider = player.AddComponent<CharacterController>();
            var playerChild = new GameObject("Audit player child collider");
            playerChild.transform.SetParent(player.transform, false);
            playerChild.transform.localPosition = Vector3.up;
            SphereCollider childCollider = playerChild.AddComponent<SphereCollider>();

            var enemyObject = new GameObject("Audit damage target");
            enemyObject.SetActive(false);
            enemyObject.transform.SetParent(fixture.transform);
            enemyObject.transform.position = origin;
            EnemyController enemy = enemyObject.AddComponent<EnemyController>();
            enemy.enabled = false;
            enemyObject.GetComponent<NavMeshAgent>().enabled = false;
            SetInteractionField(enemyConfig, "_enemyType", EnemyType.Shooter);
            SetInteractionField(enemyConfig, "_power", 3f);
            SetInteractionField(enemy, "_enemyConfig", enemyConfig);
            SetInteractionField(enemy, "_enemyGfxTransform", enemyObject.transform);
            SetInteractionField(enemy, "_currentHealth", 100f);
            var hitObject = new GameObject("Audit enemy child hitboxes");
            hitObject.transform.SetParent(enemyObject.transform, false);
            SphereCollider firstHit = hitObject.AddComponent<SphereCollider>();
            BoxCollider secondHit = hitObject.AddComponent<BoxCollider>();
            enemyObject.SetActive(true);

            var bulletObject = new GameObject("Audit bullet");
            bulletObject.transform.SetParent(fixture.transform);
            Bullet bullet = bulletObject.AddComponent<Bullet>();
            bullet.Initialization(1f, null, enemyConfig, player);
            InvokeInteraction(bullet, "OnTriggerEnter", firstHit);
            InvokeInteraction(bullet, "OnTriggerEnter", secondHit);
            Check(Mathf.Approximately(Field<float>(enemy, "_currentHealth"), 97f),
                "One bullet damages an enemy through child hitboxes only once before deferred destruction");

            var explosionObject = new GameObject("Audit explosion");
            explosionObject.transform.SetParent(fixture.transform);
            explosionObject.transform.position = origin;
            Explode explosion = explosionObject.AddComponent<Explode>();
            SetInteractionField(explosion, "_power", 3f);
            explosion.Construct(null, null);
            Physics.SyncTransforms();
            explosion.ExplodeBehaviour();
            explosion.ExplodeBehaviour();
            Check(Mathf.Approximately(Field<float>(enemy, "_currentHealth"), 94f),
                "Explosion finds the enemy parent, deduplicates its colliders, and ignores a repeated detonation");

            var door = new GameObject("Audit door");
            door.transform.SetParent(fixture.transform);
            door.transform.position = origin + Vector3.right * 20f;
            Vector3 closedPosition = door.transform.position;
            var plate = new GameObject("Audit pressure plate");
            plate.SetActive(false);
            plate.transform.SetParent(fixture.transform);
            plate.transform.position = closedPosition;
            plate.AddComponent<BoxCollider>();
            PuzzleActivator activator = plate.AddComponent<PuzzleActivator>();
            SetInteractionField(activator, "_activatorType", Raven.Puzzle.ActivatorType.Lever);
            SetInteractionField(activator, "_doorTransform", door.transform);
            SetInteractionField(activator, "_doorShift", Vector3.up * 2f);
            plate.SetActive(true);
            InvokeInteraction(activator, "OnTriggerEnter", playerCollider);
            InvokeInteraction(activator, "OnTriggerEnter", childCollider);
            activator.OpenDoor();
            bool opened = door.transform.position == closedPosition + Vector3.up * 2f;
            InvokeInteraction(activator, "OnTriggerExit", playerCollider);
            bool held = !Field<bool>(activator, "_closingDoor");
            InvokeInteraction(activator, "OnTriggerExit", childCollider);
            InvokeInteraction(activator, "CloseDoor");
            Check(opened && held && door.transform.position == closedPosition,
                "Zero-duration door reaches exact endpoints and remains open until the last player collider exits");

            SetInteractionField(activator, "_stayOpenForAWhile", true);
            SetInteractionField(activator, "_stayOpenTime", 0.05f);
            InvokeInteraction(activator, "OnTriggerEnter", playerCollider);
            activator.OpenDoor();
            InvokeInteraction(activator, "OnTriggerExit", playerCollider);
            InvokeInteraction(activator, "OnTriggerEnter", playerCollider);
            yield return new WaitForSeconds(0.1f);
            bool reopened = door.transform.position == closedPosition + Vector3.up * 2f;
            InvokeInteraction(activator, "OnTriggerExit", playerCollider);
            yield return new WaitForSeconds(0.1f);
            yield return null;
            Check(reopened && door.transform.position == closedPosition,
                "Returning to a timed pressure plate cancels pending closure; leaving again closes normally");

            InputManager input = fixture.AddComponent<InputManager>();
            input.CanInput = true;
            camera = new CameraManager(input, playerChild, null, player, player.transform, playerChild, movementConfig);
            movement = new PlayerMovementManager(player, movementConfig, player.transform, camera, input, null, player.transform);
            var resetObject = new GameObject("Audit reset volume");
            resetObject.SetActive(false);
            resetObject.transform.SetParent(fixture.transform);
            ResetPoint reset = resetObject.AddComponent<ResetPoint>();
            reset.enabled = false;

            reset.Construct(input, context.Container.Resolve<PlayerDataManager>(), movement);
            resetObject.SetActive(true);
            InvokeInteraction(reset, "Start");
            bool fallback = reset.ResetPosition == player.transform.position;
            var checkpointObject = new GameObject("Audit checkpoint");
            checkpointObject.SetActive(false);
            checkpointObject.transform.SetParent(fixture.transform);
            Checpoint checkpoint = checkpointObject.AddComponent<Checpoint>();
            var data = context.Container.Resolve<PlayerDataManager>();
            Vector3 previousCheckpoint = Field<Vector3>(data, "_checkpointPosition");
            Quaternion previousRotation = Field<Quaternion>(data, "_checkpointRotation");
            checkpoint.Construct(data);
            SetInteractionField(checkpoint, "_resetPoints", new[] { reset });
            checkpointObject.SetActive(true);
            InvokeInteraction(checkpoint, "OnTriggerEnter", childCollider);
            Vector3 savedPosition = player.transform.position;
            bool canonicalPlayer = reset.PlayerTransform == player.transform && reset.ResetPosition == savedPosition;
            bool sharedCheckpoint = Field<Vector3>(data, "_checkpointPosition") == savedPosition;
            data.SetCheckpoint(previousCheckpoint, previousRotation);
            Check(fallback && canonicalPlayer && sharedCheckpoint,
                "Checkpoint stores the player root and shares its respawn location with the lives system");
        }
        finally
        {
            movement?.Dispose();
            camera?.Dispose();
            Object.Destroy(fixture);
            Object.Destroy(enemyConfig);
            Object.Destroy(movementConfig);
        }
        yield return null;
    }

    private static void SetInteractionField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
    }

    private static void InvokeInteraction(object target, string name, params object[] arguments)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, arguments);
    }
}

