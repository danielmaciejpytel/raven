using System;
using System.Linq;
using Raven.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public sealed class RavenHolsterMotionEditor : EditorWindow
{
    [SerializeField] private RavenHolsterMotion _draft;
    [SerializeField] private RavenHolsterMotion _source;
    private RavenHolsterMotionStage _stage;
    [SerializeField] private bool _right = true, _draftRight = true;
    private bool _playing;
    private int _selected = 1;
    private float _time;
    private double _lastUpdate;
    private Slider _scrub, _clipTime;
    private VisualElement _keys;
    private Vector3Field _position, _rotation, _elbow;
    private Label _status;

    [MenuItem("Tools/Raven/Holster Motion Editor")]
    public static void Open() => GetWindow<RavenHolsterMotionEditor>("Ruch chowania broni");

    private void OnEnable()
    {
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += SceneGUI;
        Undo.undoRedoPerformed += OnUndo;
        AssemblyReloadEvents.beforeAssemblyReload += CloseStage;
    }
    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= SceneGUI;
        Undo.undoRedoPerformed -= OnUndo;
        AssemblyReloadEvents.beforeAssemblyReload -= CloseStage;
        CloseStage();
    }
    private void OnDestroy(){if(_draft!=null)DestroyImmediate(_draft);}
    private void CloseStage()
    {
        _playing = false;
        if (_stage != null && StageUtility.GetCurrentStage() == _stage) StageUtility.GoBackToPreviousStage();
        _stage = null;
    }

    public void CreateGUI()
    {
        titleContent=new GUIContent("Ruch chowania broni");
        minSize = new Vector2(410, 610);
        var root = rootVisualElement;
        root.Clear();
        var scroll = new ScrollView();
        scroll.style.paddingLeft = scroll.style.paddingRight = 12;
        scroll.style.paddingTop = scroll.style.paddingBottom = 10;
        root.Add(scroll);
        scroll.Add(new HelpBox("Ustaw 3 lub 5 klatek na kopii postaci. Początek pobierany jest z wybranej animacji, koniec z kabury. W grze początek pochodzi z aktualnej pozy ręki.", HelpBoxMessageType.Info));
        var hand = new DropdownField("Dłoń", new System.Collections.Generic.List<string>{"Prawa → lewa kabura", "Lewa → prawa kabura"}, _right ? 0 : 1);
        hand.RegisterValueChangedCallback(e =>
        {
            bool next = hand.index == 0;
            if (hasUnsavedChanges && !EditorUtility.DisplayDialog("Niezapisana trasa", "Zmiana dłoni odrzuci niezapisane ustawienia. Kontynuować?", "Odrzuć", "Wróć"))
            { hand.SetValueWithoutNotify(_right ? hand.choices[0] : hand.choices[1]); return; }
            hasUnsavedChanges=false;_right = next; OpenPreview();
        });
        scroll.Add(hand);
        scroll.Add(new Button(OpenPreview){text="Otwórz postać w Scene View"});
        if (_draft != null)
        {
            var clip = new ObjectField("Animacja startowa"){objectType=typeof(AnimationClip), allowSceneObjects=false, value=_draft.previewClip};
            clip.RegisterValueChangedCallback(e => { Change("Animacja startowa"); _draft.previewClip=(AnimationClip)e.newValue; _draft.previewTime=0f; _clipTime.highValue=_draft.previewClip != null ? _draft.previewClip.length : 1f; _clipTime.SetValueWithoutNotify(0f); RefreshPose(); });
            scroll.Add(clip);
            _clipTime = new Slider("Czas w animacji [s]",0f,_draft.previewClip != null ? _draft.previewClip.length : 1f){value=_draft.previewTime, showInputField=true};
            _clipTime.RegisterValueChangedCallback(e=>{Change("Czas animacji");_draft.previewTime=e.newValue;RefreshPose();});
            scroll.Add(_clipTime);
            var duration = new FloatField("Czas odkładania [s]"){value=_draft.duration};
            duration.RegisterValueChangedCallback(e=>{Change("Czas odkładania");_draft.duration=Mathf.Clamp(e.newValue,.2f,3f);duration.SetValueWithoutNotify(_draft.duration);});
            scroll.Add(duration);
            var count = new DropdownField("Liczba klatek",new System.Collections.Generic.List<string>{"3", "5"},_draft.keys.Length==3?0:1);
            count.RegisterValueChangedCallback(e=>ResizeKeys(count.index==0?3:5));scroll.Add(count);
            _keys = new VisualElement();_keys.style.flexDirection=FlexDirection.Row;scroll.Add(_keys);BuildKeyButtons();
            _position=new Vector3Field("Pozycja pistoletu");_rotation=new Vector3Field("Obrót pistoletu");_elbow=new Vector3Field("Punkt łokcia");
            _position.RegisterValueChangedCallback(e=>EditKey(e.newValue,null,null));
            _rotation.RegisterValueChangedCallback(e=>EditKey(null,e.newValue,null));
            _elbow.RegisterValueChangedCallback(e=>EditKey(null,null,e.newValue));
            scroll.Add(_position);scroll.Add(_rotation);scroll.Add(_elbow);
            scroll.Add(new HelpBox("Wybierz klatkę. W Scene View: W — pozycja broni, E — obrót; turkusowy uchwyt prowadzi łokieć. Pola liczbowe są względem miednicy. Start i koniec broni są automatyczne. Ctrl+Z cofa edycję.",HelpBoxMessageType.None));
            _scrub=new Slider("Przebieg ruchu",0f,1f){value=_time,showInputField=true};
            _scrub.RegisterValueChangedCallback(e=>{_playing=false;_time=e.newValue;RefreshPose();});scroll.Add(_scrub);
            scroll.Add(new Button(()=>{_playing=!_playing;if(_playing&&_time>=1f)_time=0f;_lastUpdate=EditorApplication.timeSinceStartup;}){text="Odtwórz / pauza"});
            scroll.Add(new Button(SaveMotion){text="Zapisz trasę i przypisz do Player.prefab"});
        }
        scroll.Add(new Button(CloseStage){text="Zamknij podgląd"});
        _status=new Label(_draft==null?"Otwórz podgląd, aby wybrać animację i ustawić klatki.":"Zmiany czekają na jawny zapis. Kabury pozostają na swoich miejscach.");
        _status.style.whiteSpace=WhiteSpace.Normal;scroll.Add(_status);
        UpdateFields();
    }

    public void OpenPreview()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){_status.text="Wyłącz Play Mode przed edycją.";return;}
        bool resume=_stage==null&&_draft!=null&&_draftRight==_right;
        if(!resume&&hasUnsavedChanges&&!EditorUtility.DisplayDialog("Niezapisana trasa","Ponowne otwarcie odrzuci niezapisane ustawienia.","Odrzuć","Wróć"))return;
        var prefabStage=PrefabStageUtility.GetCurrentPrefabStage();
        if(prefabStage!=null&&prefabStage.scene.isDirty){_status.text="Najpierw zapisz edytowany prefab.";return;}
        CloseStage();
        _stage=CreateInstance<RavenHolsterMotionStage>();_stage.RightHand=_right;
        StageUtility.GoToStage(_stage,true);
        _source=_stage.Slot.motion;
        if(!resume)
        {
            if(_draft!=null)DestroyImmediate(_draft);
            _draft=_source!=null?Instantiate(_source):CreateInstance<RavenHolsterMotion>();
            _draftRight=_right;
        }
        _draft.hideFlags=HideFlags.HideAndDontSave;
        if(_draft.previewClip==null)_draft.previewClip=_stage.DefaultClip;
        _stage.SampleBase(_draft.previewClip,_draft.previewTime);
        if(!_draft.IsValid)SeedKeys(5);
        _selected=1;_time=_draft.KeyTime(_selected);if(!resume)hasUnsavedChanges=false;
        CreateGUI();RefreshPose();
        var view=SceneView.lastActiveSceneView;
        if(view!=null){view.sceneLighting=true;view.LookAt(_stage.Hips.position,Quaternion.Euler(8,170,0),1.6f);}
    }

    private void SeedKeys(int count)
    {
        _draft.keys=new RavenHolsterMotion.Key[count];
        for(int i=0;i<count;i++)
        {
            float t=i/(float)(count-1);
            Pose start=_stage.Start,end=_stage.End;
            Vector3 pos=Vector3.Lerp(start.position,end.position,t);
            pos.z+=.12f*Mathf.Sin(t*Mathf.PI);
            _draft.keys[i]=new RavenHolsterMotion.Key{position=pos,rotation=Quaternion.Slerp(start.rotation,end.rotation,t).eulerAngles,
                elbow=Vector3.Lerp(_stage.StartElbow,new Vector3(_right?.23f:-.23f,.05f,.12f),t)};
        }
    }
    private void ResizeKeys(int count)
    {
        if(_stage==null)return;
        var sampled=new RavenHolsterMotion.Key[count];
        for(int i=0;i<count;i++)sampled[i]=_draft.Sample(InverseSmooth(i/(float)(count-1)),_stage.Start,_stage.End,_stage.StartElbow);
        Change("Liczba klatek");_draft.keys=sampled;_selected=Mathf.Clamp(_selected,1,count-2);CreateGUI();SelectKey(_selected);
    }
    private static float InverseSmooth(float value)
    {
        float lo=0,hi=1;for(int i=0;i<20;i++){float mid=(lo+hi)*.5f;if(Mathf.SmoothStep(0,1,mid)<value)lo=mid;else hi=mid;}return(lo+hi)*.5f;
    }
    private void BuildKeyButtons()
    {
        _keys.Clear();for(int i=0;i<_draft.keys.Length;i++){int index=i;var b=new Button(()=>SelectKey(index)){text=i==0?"Start":i==_draft.keys.Length-1?"Kabura":$"Klatka {i+1}"};b.style.flexGrow=1;_keys.Add(b);}
    }
    private void SelectKey(int index){_selected=index;_playing=false;_time=_draft.KeyTime(index);_scrub?.SetValueWithoutNotify(_time);RefreshPose();}
    private void Change(string label){Undo.RecordObject(_draft,label);hasUnsavedChanges=true;saveChangesMessage="Zapisać trasę chowania broni do prefabu?";}
    private void EditKey(Vector3? position,Vector3? rotation,Vector3? elbow)
    {
        if(_draft==null||_stage==null)return;
        Change("Klatka chowania broni");var key=_draft.keys[_selected];
        if(position.HasValue)key.position=position.Value;if(rotation.HasValue)key.rotation=rotation.Value;if(elbow.HasValue)key.elbow=elbow.Value;
        _draft.keys[_selected]=key;SelectKey(_selected);
    }
    private void UpdateFields()
    {
        if(_draft==null||_position==null||_stage==null)return;
        var key=_draft.keys[_selected];
        Pose pose=_selected==0?_stage.Start:_selected==_draft.keys.Length-1?_stage.End:key.Pose;
        _position.SetValueWithoutNotify(pose.position);_rotation.SetValueWithoutNotify(pose.rotation.eulerAngles);_elbow.SetValueWithoutNotify(_selected==0?_stage.StartElbow:key.elbow);
        bool middle=_selected>0&&_selected<_draft.keys.Length-1;_position.SetEnabled(middle);_rotation.SetEnabled(middle);_elbow.SetEnabled(_selected>0);
    }
    private void RefreshPose()
    {
        if(_stage==null||_draft==null)return;
        _stage.SampleBase(_draft.previewClip,_draft.previewTime);
        _stage.Apply(_draft,_time);UpdateFields();SceneView.RepaintAll();
    }
    private void OnUndo()
    {
        if(_draft==null)return;
        _selected=Mathf.Clamp(_selected,0,_draft.keys.Length-1);
        CreateGUI();RefreshPose();
    }
    private void Tick()
    {
        if(!_playing||_stage==null||_draft==null)return;
        double now=EditorApplication.timeSinceStartup;_time+=Mathf.Min(.1f,(float)(now-_lastUpdate))/Mathf.Max(.2f,_draft.duration);_lastUpdate=now;
        if(_time>=1f){_time=1f;_playing=false;}_scrub?.SetValueWithoutNotify(_time);RefreshPose();
    }
    private void SceneGUI(SceneView view)
    {
        if(_stage==null||_draft==null||StageUtility.GetCurrentStage()!=_stage)return;
        var hips=_stage.Hips;Handles.color=new Color(1f,.7f,.1f);
        var points=new Vector3[61];for(int i=0;i<points.Length;i++)points[i]=hips.TransformPoint(_draft.Sample(i/60f,_stage.Start,_stage.End,_stage.StartElbow).position);
        Handles.DrawAAPolyLine(3f,points);
        for(int i=0;i<_draft.keys.Length;i++)
        {
            Vector3 p=hips.TransformPoint(i==0?_stage.Start.position:i==_draft.keys.Length-1?_stage.End.position:_draft.keys[i].position);
            float size=HandleUtility.GetHandleSize(p)*.06f;
            if(Handles.Button(p,Quaternion.identity,size,size,Handles.SphereHandleCap))SelectKey(i);
            Handles.Label(p,$"  {i+1}");
        }
        if(_playing)return;
        var key=_draft.keys[_selected];
        if(_selected>0&&_selected<_draft.keys.Length-1)
        {
            Vector3 p=hips.TransformPoint(key.position);Quaternion q=hips.rotation*key.Pose.rotation;
            EditorGUI.BeginChangeCheck();
            if(Tools.current==Tool.Rotate)q=Handles.RotationHandle(q,p);else p=Handles.PositionHandle(p,Tools.pivotRotation==PivotRotation.Local?q:Quaternion.identity);
            if(EditorGUI.EndChangeCheck())EditKey(hips.InverseTransformPoint(p),(Quaternion.Inverse(hips.rotation)*q).eulerAngles,null);
        }
        if(_selected>0)
        {
            Handles.color=Color.cyan;Vector3 p=hips.TransformPoint(key.elbow);Handles.Label(p,"  Łokieć");
            EditorGUI.BeginChangeCheck();var moved=Handles.FreeMoveHandle(p,HandleUtility.GetHandleSize(p)*.055f,Vector3.zero,Handles.SphereHandleCap);
            if(EditorGUI.EndChangeCheck())EditKey(null,null,hips.InverseTransformPoint(moved));
        }
    }
    public override void SaveChanges(){SaveMotion();if(!hasUnsavedChanges)base.SaveChanges();}
    public override void DiscardChanges(){hasUnsavedChanges=false;base.DiscardChanges();}
    private void SaveMotion()
    {
        if(_draft==null||_stage==null||EditorApplication.isPlayingOrWillChangePlaymode)return;
        string path=_source!=null?AssetDatabase.GetAssetPath(_source):$"Assets/Graphics/Characters/Raven/Animations/{(_right?"Right":"Left")}HolsterMotion.asset";
        if(_source==null)
        {
            path=AssetDatabase.GenerateUniqueAssetPath(path);_source=Instantiate(_draft);_source.hideFlags=HideFlags.None;AssetDatabase.CreateAsset(_source,path);
        }
        else{Undo.RecordObject(_source,"Zapis trasy chowania");EditorUtility.CopySerialized(_draft,_source);_source.hideFlags=HideFlags.None;EditorUtility.SetDirty(_source);}
        AssetDatabase.SaveAssetIfDirty(_source);
        AssignToPrefab(RavenHolsterPreviewStage.PrefabPath,_source,_right);
        hasUnsavedChanges=false;_status.text="Zapisano trasę. Player.prefab korzysta z niej podczas chowania broni.";
    }
    public static void AssignToPrefab(string prefabPath,RavenHolsterMotion motion,bool right)
    {
        var prefab=PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var serialized=new SerializedObject(prefab.GetComponentInChildren<RavenWeaponHolster>(true));
            serialized.FindProperty(right?"_right":"_left").FindPropertyRelative("motion").objectReferenceValue=motion;
            serialized.ApplyModifiedPropertiesWithoutUndo();PrefabUtility.SaveAsPrefabAsset(prefab,prefabPath);
        }
        finally{PrefabUtility.UnloadPrefabContents(prefab);}
    }
}

public sealed class RavenHolsterMotionStage : PreviewSceneStage
{
    public bool RightHand=true;
    public RavenWeaponHolster.WeaponSlot Slot {get;private set;}
    public Transform Hips {get;private set;}
    public Pose Start {get;private set;}
    public Pose End => new Pose(Hips.InverseTransformPoint(Slot.holster.position),Quaternion.Inverse(Hips.rotation)*Slot.holster.rotation);
    public Vector3 StartElbow {get;private set;}
    public AnimationClip DefaultClip {get;private set;}
    private GameObject _model;
    private Transform[] _bones;
    private Vector3[] _positions,_scales;
    private Quaternion[] _rotations;
    private RavenPistolGrip _grip;
    protected override GUIContent CreateHeaderContent()=>new GUIContent("Raven — klatki chowania broni");
    protected override bool OnOpenStage()
    {
        if(!base.OnOpenStage())return false;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(RavenHolsterPreviewStage.PrefabPath);
        _model=Instantiate(prefab.GetComponentInChildren<RavenWeaponHolster>(true).gameObject);
        SceneManager.MoveGameObjectToScene(_model,scene);_model.name="Raven — edycja ruchu (kopia)";
        _model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        foreach(var b in _model.GetComponentsInChildren<Behaviour>(true))b.enabled=false;
        foreach(var p in _model.GetComponentsInChildren<ParticleSystem>(true))p.gameObject.SetActive(false);
        foreach(var renderer in _model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {renderer.updateWhenOffscreen=true;renderer.forceMatrixRecalculationPerRender=true;}
        var animator=_model.GetComponent<Animator>();animator.Rebind();
        Hips=animator.GetBoneTransform(HumanBodyBones.Hips);_grip=_model.GetComponent<RavenPistolGrip>();
        var config=new SerializedObject(_model.GetComponent<RavenWeaponHolster>());var field=config.FindProperty(RightHand?"_right":"_left");
        Slot=new RavenWeaponHolster.WeaponSlot{weapon=(Transform)field.FindPropertyRelative("weapon").objectReferenceValue,holster=(Transform)field.FindPropertyRelative("holster").objectReferenceValue,motion=(RavenHolsterMotion)field.FindPropertyRelative("motion").objectReferenceValue,
            upper=animator.GetBoneTransform(RightHand?HumanBodyBones.RightUpperArm:HumanBodyBones.LeftUpperArm),lower=animator.GetBoneTransform(RightHand?HumanBodyBones.RightLowerArm:HumanBodyBones.LeftLowerArm),hand=animator.GetBoneTransform(RightHand?HumanBodyBones.RightHand:HumanBodyBones.LeftHand)};
        Slot.weapon.gameObject.SetActive(true);
        Slot.handOffset=Quaternion.Inverse(Slot.hand.rotation)*(Slot.weapon.position-Slot.hand.position);
        Slot.handRotation=Quaternion.Inverse(Slot.hand.rotation)*Slot.weapon.rotation;
        _bones=_model.GetComponentsInChildren<Transform>(true);_positions=_bones.Select(t=>t.localPosition).ToArray();_rotations=_bones.Select(t=>t.localRotation).ToArray();_scales=_bones.Select(t=>t.localScale).ToArray();
        DefaultClip=animator.runtimeAnimatorController.animationClips.FirstOrDefault(c=>c.name.IndexOf("Idle",StringComparison.OrdinalIgnoreCase)>=0);
        Light("Key",Quaternion.Euler(35,160,0),2.2f);Light("Fill",Quaternion.Euler(20,-35,0),1.2f);
        return true;
    }
    private void Light(string name,Quaternion rotation,float intensity)
    {
        var light=new GameObject(name).AddComponent<Light>();SceneManager.MoveGameObjectToScene(light.gameObject,scene);light.type=LightType.Directional;light.intensity=intensity;light.transform.rotation=rotation;light.gameObject.hideFlags=HideFlags.HideInHierarchy;
    }
    public void SampleBase(AnimationClip clip,float time)
    {
        for(int i=0;i<_bones.Length;i++){_bones[i].localPosition=_positions[i];_bones[i].localRotation=_rotations[i];_bones[i].localScale=_scales[i];}
        if(clip!=null)clip.SampleAnimation(_model,Mathf.Clamp(time,0,clip.length));
        _model.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        _grip.ApplyPreviewPose();Slot.carryWristLocal=Slot.hand.localRotation;
        Start=new Pose(Hips.InverseTransformPoint(Slot.weapon.position),Quaternion.Inverse(Hips.rotation)*Slot.weapon.rotation);
        StartElbow=Hips.InverseTransformPoint(Slot.lower.position);
    }
    public void Apply(RavenHolsterMotion motion,float time)
    {
        var pose=motion.Sample(time,Start,End,StartElbow);
        RavenWeaponHolster.PreviewWeaponPose(Slot,new Pose(Hips.TransformPoint(pose.position),Hips.rotation*pose.Pose.rotation),Hips.TransformPoint(pose.elbow));
    }
}
