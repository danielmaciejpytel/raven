using System.Collections;
using System.Linq;
using Raven.Manager;
using Raven.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Object = UnityEngine.Object;

public static partial class RavenScriptAudit
{
    private static IEnumerator CheckAnimationAimMask()
    {
        GameObject model = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player.prefab");
            model = Object.Instantiate(prefab.transform.Find("Raven").gameObject);
            model.name = "Raven animation audit fixture";
            model.hideFlags = HideFlags.DontSave;
            model.transform.SetPositionAndRotation(new Vector3(2000f, 1000f, 0f), Quaternion.identity);
            var animator = model.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            model.GetComponent<RavenFootIk>().enabled = false;
            var right = model.GetComponentsInChildren<TwoBoneIKConstraint>().Single(x =>
                x.data.tip == animator.GetBoneTransform(HumanBodyBones.RightHand));
            var activeRig = right.GetComponentInParent<Rig>();
            var rigs = model.GetComponentsInChildren<Rig>();
            animator.SetBool("Aim", true);
            animator.SetBool("Grounded", true);
            animator.SetFloat("TurnPlaybackRate", 1f);
            animator.SetFloat("StationaryTurnAngle", 90f);
            int layer = animator.GetLayerIndex("Turn In Place");
            foreach (float weight in new[] { 0f, 1f })
            {
                animator.SetLayerWeight(layer, weight);
                float maxAngle = 0f;
                float maxTargetDrift = 0f;
                for (int frame = 0; frame < 45; frame++)
                {
                    foreach (var rig in rigs) rig.weight = rig == activeRig ? 1f : 0f;
                    Vector3 target = right.data.root.position + Vector3.forward * 10f;
                    right.data.target.position = target;
                    yield return new WaitForEndOfFrame();
                    if (frame < 30) continue;
                    maxTargetDrift = Mathf.Max(maxTargetDrift, Vector3.Distance(target, right.data.target.position));
                    maxAngle = Mathf.Max(maxAngle, Vector3.Angle(right.data.tip.position - right.data.root.position,
                        target - right.data.root.position));
                }
                Check(maxTargetDrift < 0.01f && maxAngle < 3f,
                    $"Aim hand and target survive turn layer weight {weight} (error {maxAngle:F2} deg, target drift {maxTargetDrift:F4}m)");
            }

            animator.SetLayerWeight(layer, 0f);
            var camera = new GameObject("Aim release camera");
            camera.transform.SetParent(model.transform, false);
            var ballistic = new GameObject("Aim release ballistic target");
            ballistic.transform.SetParent(model.transform, false);
            using (var manager = new PlayerRigManager(null, new[] { activeRig }, ballistic, 0, camera.transform))
            {
                manager.SetAimState(true);
                for (int frame = 0; frame < 30; frame++)
                {
                    manager.Tick();
                    right.data.target.position = right.data.root.position + model.transform.forward * 10f;
                    yield return new WaitForEndOfFrame();
                    manager.LateTick();
                }
                Quaternion lastAim = right.data.root.localRotation;
                manager.SetAimState(false);
                animator.SetBool("Aim", false);
                bool immediate = activeRig.weight == 0f;
                bool startsMoving = false;
                float finalPoseError = 0f;
                float returnElapsed = 0f;
                for (int frame = 0; frame < 40 || returnElapsed < 0.3f; frame++)
                {
                    // Both the camera target and world-space body heading change.
                    // The release must still start from the last local arm pose.
                    camera.transform.Rotate(0f, 29f, 0f);
                    model.transform.Rotate(0f, 7f, 0f);
                    manager.Tick();
                    yield return new WaitForEndOfFrame();
                    Quaternion animationPose = right.data.root.localRotation;
                    manager.LateTick();
                    if (frame == 0) startsMoving = Quaternion.Angle(lastAim, right.data.root.localRotation) > 0.01f;
                    finalPoseError = Quaternion.Angle(animationPose, right.data.root.localRotation);
                    returnElapsed += Time.deltaTime;
                }
                Check(immediate && startsMoving, "Aim release starts on the first evaluated frame and disconnects camera-driven IK");
                Check(finalPoseError < 0.01f && activeRig.weight == 0f,
                    "Arm return hands full control back to animation after camera and body rotations");
            }
        }
        finally
        {
            if (model != null) Object.DestroyImmediate(model);
        }
    }
    private static IEnumerator CheckAimEntryCamera()
    {
        var host = new GameObject("Aim entry camera audit");
        var config = ScriptableObject.CreateInstance<Raven.Config.MovementConfig>();
        CameraManager camera = null;
        try
        {
            GameObject Child(string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(host.transform, false);
                return child;
            }
            var input = host.AddComponent<Raven.Input.InputManager>();
            Field<Controls>(input, "_controls").devices = new UnityEngine.InputSystem.InputDevice[0];
            input.CanInput = true;
            var body = Child("Body");
            var view = Child("View");
            var aimCamera = Child("Aim camera");
            var aimLock = Child("Aim lock");
            aimLock.transform.SetParent(body.transform, false);
            camera = new CameraManager(input, aimCamera, null, body, view.transform, aimLock, config);
            var setCameras = typeof(CameraManager).GetMethod("SetCameras",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (float angle in new[] { -170f, -90f, 20f, 90f, 170f })
            {
                setCameras.Invoke(camera, new object[] { false });
                body.transform.rotation = Quaternion.identity;
                view.transform.rotation = Quaternion.Euler(0f, angle, 0f);
                camera.OnPlayerTeleported();
                yield return null;
                setCameras.Invoke(camera, new object[] { true });
                float firstYaw = Mathf.Abs(Mathf.DeltaAngle(0f, body.transform.eulerAngles.y));
                Check(camera.AimEntryActive && firstYaw < Mathf.Abs(angle) * 0.5f &&
                      Mathf.Abs(Mathf.DeltaAngle(aimLock.transform.eulerAngles.y, angle)) < 0.1f &&
                      (Mathf.Abs(angle) < 90f || camera.AimPoseWeight < 0.15f),
                    $"Aim entry {angle} points the camera immediately while body and hand enter gradually");
                float elapsed = 0f;
                while (camera.AimEntryActive && elapsed < 0.7f)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                    setCameras.Invoke(camera, new object[] { true });
                }
                Check(!camera.AimEntryActive && Mathf.Abs(Mathf.DeltaAngle(body.transform.eulerAngles.y, angle)) < 0.1f,
                    $"Aim entry {angle} reaches the requested body heading without root motion");
            }
            setCameras.Invoke(camera, new object[] { false });
            body.transform.rotation = Quaternion.identity;
            view.transform.rotation = Quaternion.Euler(0f, 170f, 0f);
            camera.OnPlayerTeleported();
            setCameras.Invoke(camera, new object[] { true });
            Quaternion partial = body.transform.rotation;
            setCameras.Invoke(camera, new object[] { false });
            Check(!camera.AimEntryActive && Quaternion.Angle(partial, body.transform.rotation) < 0.01f,
                "Releasing aim cancels entry without snapping to its unfinished target");
        }
        finally
        {
            camera?.Dispose();
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(config);
        }
    }

}
