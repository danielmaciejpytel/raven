using System;
using System.Linq;
using Raven.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class RavenHolsterPreview : EditorWindow
{
    private Label _status;

    private void OnEnable() => AssemblyReloadEvents.beforeAssemblyReload += ClosePreview;
    private void OnDisable()
    {
        AssemblyReloadEvents.beforeAssemblyReload -= ClosePreview;
        ClosePreview();
    }
    private static void ClosePreview()
    {
        if (StageUtility.GetCurrentStage() is RavenHolsterPreviewStage) StageUtility.GoBackToPreviousStage();
    }

    [MenuItem("Tools/Raven/Holster Preview")]
    public static void Open() => GetWindow<RavenHolsterPreview>("Kabury Raven");

    [MenuItem("CONTEXT/RavenWeaponHolster/Podgląd kabur w T-pose")]
    private static void OpenContext(MenuCommand command) => Open();

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.style.paddingLeft = root.style.paddingRight = 12;
        root.style.paddingTop = root.style.paddingBottom = 12;
        root.Add(new HelpBox("Podgląd kopii Player.prefab w T-pose. Pistolety pokazują dokładne położenie po schowaniu. Scena i kości oryginału pozostają bez zmian.", HelpBoxMessageType.Info));
        root.Add(new Button(StartPreview) { text = "Otwórz podgląd T-pose" });
        root.Add(new Button(() => SelectSocket(true)) { text = "Prawa kabura" });
        root.Add(new Button(() => SelectSocket(false)) { text = "Lewa kabura" });
        root.Add(new HelpBox("W Scene View: W — przesuwanie, E — obrót, F — zbliżenie. W Inspectorze możesz wpisać dokładną pozycję i obrót. Zapis obejmuje wyłącznie oba punkty kabur, bez skali. Zamknięcie podglądu odrzuca niezapisane ustawienia.", HelpBoxMessageType.None));
        root.Add(new Button(() =>
        {
            var stage = StageUtility.GetCurrentStage() as RavenHolsterPreviewStage;
            if (stage == null) { _status.text = "Najpierw otwórz podgląd."; return; }
            stage.ApplySockets();
            _status.text = "Zapisano oba punkty w Player.prefab.";
        }) { text = "Zapisz punkty do prefabu" });
        root.Add(new Button(() =>
        {
            if (StageUtility.GetCurrentStage() is RavenHolsterPreviewStage) StageUtility.GoBackToPreviousStage();
            _status.text = "Podgląd zamknięty.";
        }) { text = "Zamknij podgląd" });
        _status = new Label("Ustawienia zapisujesz świadomie przyciskiem powyżej.");
        _status.style.whiteSpace = WhiteSpace.Normal;
        root.Add(_status);
        minSize = new Vector2(340, 320);
    }

    public void StartPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            _status.text = "Wyłącz Play Mode przed ustawianiem kabur.";
            return;
        }
        if (!(StageUtility.GetCurrentStage() is RavenHolsterPreviewStage))
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null && prefabStage.scene.isDirty)
            {
                _status.text = "Najpierw zapisz lub zamknij edytowany prefab, aby podgląd używał aktualnych ustawień.";
                return;
            }
            StageUtility.GoToStage(CreateInstance<RavenHolsterPreviewStage>(), true);
        }
        SelectSocket(true);
        var view = SceneView.lastActiveSceneView;
        if (view != null)
        {
            var stage = StageUtility.GetCurrentStage() as RavenHolsterPreviewStage;
            view.sceneLighting = true;
            if (stage != null) view.LookAt(stage.PreviewBounds.center, Quaternion.Euler(10, 155, 0), 1.65f);
        }
        _status.text = "Wybierz kaburę i ustaw pistolet w Scene View. Zmiany czekają na zapis.";
    }

    private void SelectSocket(bool right)
    {
        var stage = StageUtility.GetCurrentStage() as RavenHolsterPreviewStage;
        if (stage == null) { _status.text = "Najpierw otwórz podgląd."; return; }
        Selection.activeGameObject = (right ? stage.Right : stage.Left).gameObject;
        Tools.current = Tool.Move;
        Tools.pivotMode = PivotMode.Pivot;
        SceneView.lastActiveSceneView?.Focus();
    }
}

public sealed class RavenHolsterPreviewStage : PreviewSceneStage
{
    public const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
    public Transform Right { get; private set; }
    public Transform Left { get; private set; }
    public Bounds PreviewBounds { get; private set; }
    private string _rightPath, _leftPath;
    private GameObject _model;

    protected override GUIContent CreateHeaderContent() => new GUIContent("Raven — kabury / T-pose");

    protected override bool OnOpenStage()
    {
        if (!base.OnOpenStage()) return false;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var source = prefab.GetComponentInChildren<RavenWeaponHolster>(true);
        _model = Instantiate(source.gameObject);
        _model.name = "Raven — podgląd kabur (nie zapisuje się do sceny)";
        SceneManager.MoveGameObjectToScene(_model, scene);
        _model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var behaviour in _model.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
        foreach (var particles in _model.GetComponentsInChildren<ParticleSystem>(true)) particles.gameObject.SetActive(false);

        // Copy the imported skeleton's reference pose, not a sampled idle or zero-muscle pose.
        var animator = _model.GetComponent<Animator>();
        string avatarPath = AssetDatabase.GetAssetPath(animator.avatar);
        var importer = AssetImporter.GetAtPath(avatarPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Nie znaleziono modelu źródłowego Avataru Raven.");
        var skeleton = importer.humanDescription.skeleton.ToDictionary(b => b.name, b => b);
        foreach (var bone in _model.GetComponentsInChildren<Transform>(true))
            if (bone != _model.transform && skeleton.TryGetValue(bone.name, out var pose))
            {
                bone.localPosition = pose.position;
                bone.localRotation = pose.rotation;
                bone.localScale = pose.scale;
            }

        var config = new SerializedObject(_model.GetComponent<RavenWeaponHolster>());
        var first = PrepareWeapon(config.FindProperty("_right"));
        var second = PrepareWeapon(config.FindProperty("_left"));
        Right = first.name == "RightHolsterSocket" ? first : second;
        Left = first.name == "LeftHolsterSocket" ? first : second;
        _rightPath = AnimationUtility.CalculateTransformPath(Right, _model.transform);
        _leftPath = AnimationUtility.CalculateTransformPath(Left, _model.transform);
        var renderers = _model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
        PreviewBounds = renderers[0].bounds;
        foreach (var renderer in renderers) { var bounds = PreviewBounds; bounds.Encapsulate(renderer.bounds); PreviewBounds = bounds; }
        AddLight("Światło podglądu", Quaternion.Euler(35, 160, 0), 2.2f);
        AddLight("Wypełnienie podglądu", Quaternion.Euler(20, -35, 0), 1.2f);
        return true;
    }

    private void AddLight(string name, Quaternion rotation, float intensity)
    {
        var light = new GameObject(name).AddComponent<Light>();
        SceneManager.MoveGameObjectToScene(light.gameObject, scene);
        light.gameObject.hideFlags = HideFlags.HideInHierarchy;
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.transform.rotation = rotation;
    }

    private static Transform PrepareWeapon(SerializedProperty slot)
    {
        var weapon = (Transform)slot.FindPropertyRelative("weapon").objectReferenceValue;
        var holster = (Transform)slot.FindPropertyRelative("holster").objectReferenceValue;
        weapon.SetParent(holster, false);
        weapon.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        weapon.gameObject.SetActive(true);
        return holster;
    }

    public void ApplySockets()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var model = prefab.GetComponentInChildren<RavenWeaponHolster>(true).transform;
            foreach (var pair in new[] { (Right, _rightPath), (Left, _leftPath) })
            {
                var target = model.Find(pair.Item2);
                if (target == null) throw new InvalidOperationException("Brak punktu kabury w prefabie.");
                target.SetLocalPositionAndRotation(pair.Item1.localPosition, pair.Item1.localRotation);
            }
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
    }
}
