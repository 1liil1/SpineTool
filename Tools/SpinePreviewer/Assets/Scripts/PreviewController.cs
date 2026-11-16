using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

public sealed class PreviewController : MonoBehaviour
{
    private PreviewLoadedAsset loaded;
    private string resourceDirectory;
    private FileSystemWatcher watcher;
    private volatile bool reloadRequested;
    private IReadOnlyList<PreviewResourceEntry> resourceEntries = Array.Empty<PreviewResourceEntry>();
    private string catalogDirectory = string.Empty;
    private string selectedResourceName = string.Empty;
    private string loadError = string.Empty;
    private float speed = 1f;
    private float zoom = 1f;
    private bool tintBlack = true;
    private bool playing = true;
    private Vector3 dragOrigin;
    private Vector3 objectOrigin;
    private Camera previewCamera;
    private Color background = Color.gray;
    private Color customBackground = new Color(0.2f, 0.25f, 0.3f);
    private bool checkerActive;
    private Texture2D checkerTexture;
    private GameObject checkerPlane;
    private Material checkerMaterial;
    private string displayMode = "";
    private readonly Queue<string> eventLog = new();
    private const int MaxEventLogCount = 200;
    private bool showEventLog = true;
    private Rect controlRect;
    private Rect eventLogRect;
    private Vector2 controlScroll;
    private GUIStyle menuScrollStyle;
    private GUIStyle wrappedLabel;
    private GUIStyle resourceButton;
    private readonly List<PreviewHistoryEntry> previewHistory = new();
    private const string DefaultFollowedBoneName = "BulletPostion";
    private string boneNameInput = DefaultFollowedBoneName;
    private string followedBoneName = string.Empty;
    private bool followDefaultBone = true;
    private string boneFollowResourceDirectory = string.Empty;
    private Spine.Bone followedBone;
    private GameObject boneMarker;
    private Material boneMarkerMaterial;
    private Vector3 followedBoneWorldPosition;
    private bool boneLookupMessageIsError;
    private string boneLookupMessage = string.Empty;

    [Serializable]
    private sealed class PreviewHistoryStore
    {
        public PreviewHistoryEntry[] Items = Array.Empty<PreviewHistoryEntry>();
    }

    [Serializable]
    private sealed class PreviewHistoryEntry
    {
        public string Path = string.Empty;
        public string LastUsedAt = string.Empty;
    }

    private const string EventLogPreference = "IdleGearDefence.SpinePreviewer.EventLogVisible";

    private void Start()
    {
        previewCamera = Camera.main;
        if (previewCamera == null)
        {
            var cameraObject = new GameObject("PreviewCamera");
            previewCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
        }
        previewCamera.orthographic = true;
        previewCamera.transform.position = new Vector3(0f, 0f, -10f);
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = background;
        CreateCheckerboard();
        showEventLog = PlayerPrefs.GetInt(EventLogPreference, 1) != 0;
        LoadPreviewHistory();
        resourceDirectory = ReadInputDirectory();
        displayMode = ReadInputArgument("--display-mode");
        if (!string.IsNullOrWhiteSpace(resourceDirectory)) LoadResource();
    }

    private void Update()
    {
        if (reloadRequested)
        {
            reloadRequested = false;
            LoadResource();
        }
        if (loaded == null) return;

        var animationState = loaded.AnimationState;
        if (animationState != null) animationState.TimeScale = playing ? speed : 0f;

        if (Input.GetMouseButtonDown(0) && !IsPointerOverUi(Input.mousePosition))
        {
            dragOrigin = Input.mousePosition;
            objectOrigin = loaded.Root.transform.position;
        }
        else if (Input.GetMouseButton(0) && !IsPointerOverUi(Input.mousePosition))
        {
            var delta = Input.mousePosition - dragOrigin;
            var candidate = objectOrigin + new Vector3(delta.x, delta.y, 0) * 0.01f * zoom;
            if (!float.IsNaN(candidate.x) && !float.IsInfinity(candidate.x) && !float.IsNaN(candidate.y) && !float.IsInfinity(candidate.y))
                loaded.Root.transform.position = candidate;
        }

        var wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > 0.01f && !IsPointerOverControlMenu(Input.mousePosition))
        {
            var previousZoom = zoom;
            var previousSize = previewCamera.orthographicSize;
            zoom *= Mathf.Pow(1.1f, wheel);
            var nextSize = 5f / zoom;
            if (float.IsNaN(zoom) || float.IsInfinity(zoom) || float.IsNaN(nextSize) || float.IsInfinity(nextSize) || zoom == 0f || nextSize == 0f)
            {
                zoom = previousZoom;
                previewCamera.orthographicSize = previousSize;
            }
            else
            {
                previewCamera.orthographicSize = nextSize;
                UpdateCheckerPlane();
            }
        }

    }

    private void LateUpdate()
    {
        UpdateBoneMarker();
    }

    private void OnGUI()
    {
        controlRect = new Rect(12, 12, 380, Screen.height - 24);
        EnsureMenuStyles();
        ApplyControlMenuScroll();
        GUILayout.BeginArea(controlRect, GUI.skin.box);
        controlScroll = GUILayout.BeginScrollView(controlScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, menuScrollStyle);
        GUILayout.Label("Spine Previewer", GUI.skin.label);
        if (GUILayout.Button("选择资源文件夹"))
        {
            var selected = WindowsFolderPicker.PickFolder();
            if (!string.IsNullOrWhiteSpace(selected))
            {
                resourceDirectory = selected;
                LoadResource();
            }
        }
        GUILayout.Label(resourceDirectory ?? "未加载资源", wrappedLabel);
        DrawResourceSelector();
        if (!string.IsNullOrWhiteSpace(loadError)) GUILayout.Label(loadError, wrappedLabel);
        if (GUILayout.Button("重新加载")) LoadResource();
        if (GUILayout.Button(tintBlack ? "双色调：开" : "双色调：关"))
        {
            tintBlack = !tintBlack;
            if (loaded != null) loaded.SetTintBlack(tintBlack);
        }
        if (loaded != null)
        {
            DrawBoneMarkerControls();
            GUILayout.Label("动画");
            foreach (var animation in loaded.AnimationNames)
            {
                if (GUILayout.Button(animation)) Play(animation, true);
            }
            GUILayout.Label("Skin");
            foreach (var skin in loaded.SkinNames)
            {
                if (GUILayout.Button(skin)) SetSkin(skin);
            }
        }
        GUILayout.Label("速度 " + speed.ToString("F2"));
        speed = GUILayout.HorizontalSlider(speed, 0f, 3f);
        if (GUILayout.Button(playing ? "暂停" : "播放")) playing = !playing;
        GUILayout.BeginHorizontal();
        GUILayout.Label("缩放 " + zoom.ToString("F3"), GUILayout.ExpandWidth(true));
        if (GUILayout.Button("重置", GUILayout.Width(72))) ResetZoom();
        GUILayout.EndHorizontal();
        GUILayout.Label("指针在画面上时滚轮缩放，无上下限");
        if (GUILayout.Button("自动居中")) Center();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("黑")) SetBackground(Color.black);
        if (GUILayout.Button("白")) SetBackground(Color.white);
        if (GUILayout.Button("灰")) SetBackground(Color.gray);
        if (GUILayout.Button("棋盘")) SetCheckerboard();
        GUILayout.EndHorizontal();
        GUILayout.Label("自定义背景（实时）");
        var oldCustom = customBackground;
        customBackground.r = GUILayout.HorizontalSlider(customBackground.r, 0f, 1f);
        customBackground.g = GUILayout.HorizontalSlider(customBackground.g, 0f, 1f);
        customBackground.b = GUILayout.HorizontalSlider(customBackground.b, 0f, 1f);
        if (oldCustom != customBackground) SetBackground(customBackground);
        DrawPreviewHistory();
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        DrawBoneMarkerLabel();

        eventLogRect = new Rect(Screen.width - 632, Screen.height - 232, 620, 220);
        if (showEventLog)
        {
            GUILayout.BeginArea(eventLogRect, GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spine 事件日志", GUI.skin.label);
            if (GUILayout.Button("关闭", GUILayout.Width(60)))
            {
                showEventLog = false;
                PlayerPrefs.SetInt(EventLogPreference, 0);
                PlayerPrefs.Save();
            }
            if (GUILayout.Button("清空", GUILayout.Width(60))) eventLog.Clear();
            GUILayout.EndHorizontal();
            foreach (var line in eventLog.Reverse().Take(8)) GUILayout.Label(line);
            GUILayout.EndArea();
        }
        else
        {
            var openRect = new Rect(Screen.width - 140, Screen.height - 42, 128, 30);
            GUILayout.BeginArea(openRect);
            if (GUILayout.Button("打开事件日志"))
            {
                showEventLog = true;
                PlayerPrefs.SetInt(EventLogPreference, 1);
                PlayerPrefs.Save();
            }
            GUILayout.EndArea();
        }
    }

    private void EnsureMenuStyles()
    {
        if (menuScrollStyle != null) return;
        menuScrollStyle = new GUIStyle(GUI.skin.scrollView);
        menuScrollStyle.normal.background = null;
        wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
        resourceButton = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleLeft };
    }

    private void DrawResourceSelector()
    {
        if (resourceEntries.Count == 0) return;
        GUILayout.Label($"目录内资源（{resourceEntries.Count}）");
        foreach (var resource in resourceEntries)
        {
            var selected = string.Equals(resource.Name, selectedResourceName, StringComparison.OrdinalIgnoreCase);
            if (GUILayout.Button((selected ? "● " : "○ ") + resource.Name, resourceButton, GUILayout.MinHeight(28)) && !selected)
            {
                selectedResourceName = resource.Name;
                // Defer until Update so animation/skin controls keep a stable layout this frame.
                reloadRequested = true;
            }
        }
    }

    private void ApplyControlMenuScroll()
    {
        var current = Event.current;
        if (current.type != EventType.ScrollWheel || !controlRect.Contains(current.mousePosition)) return;
        controlScroll.y += current.delta.y * 24f;
        current.Use();
    }

    private bool IsPointerOverControlMenu(Vector3 mousePosition)
    {
        var point = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        return controlRect.Contains(point);
    }

    private bool IsPointerOverUi(Vector3 mousePosition)
    {
        var point = new Vector2(mousePosition.x, Screen.height - mousePosition.y);
        return controlRect.Contains(point) || showEventLog && eventLogRect.Contains(point)
            || !showEventLog && new Rect(Screen.width - 150, Screen.height - 50, 150, 50).Contains(point);
    }

    private void DrawPreviewHistory()
    {
        if (previewHistory.Count == 0) return;
        GUILayout.Label("历史预览");
        foreach (var history in previewHistory.Take(8))
        {
            var label = history.Path.Length > 48 ? "…" + history.Path[^48..] : history.Path;
            if (GUILayout.Button(label))
            {
                resourceDirectory = history.Path;
                LoadResource();
            }
        }
        if (GUILayout.Button("清空历史"))
        {
            previewHistory.Clear();
            SavePreviewHistory();
        }
    }

    private void DrawBoneMarkerLabel()
    {
        if (previewCamera == null || followedBone == null) return;
        var screenPosition = previewCamera.WorldToScreenPoint(followedBoneWorldPosition);
        if (screenPosition.z <= 0f) return;
        var guiPosition = new Vector2(screenPosition.x + 10f, Screen.height - screenPosition.y - 12f);
        GUI.color = new Color(1f, 0.35f, 0.2f, 1f);
        GUI.Label(new Rect(guiPosition.x, guiPosition.y, 300f, 24f),
            $"● {followedBoneName}  ({followedBoneWorldPosition.x:F3}, {followedBoneWorldPosition.y:F3})");
        GUI.color = Color.white;
    }

    private void DrawBoneMarkerControls()
    {
        GUILayout.Label("骨骼点位置标记");
        GUILayout.Label("输入任意骨骼名，或选择常见发射点");
        boneNameInput = GUILayout.TextField(boneNameInput ?? string.Empty);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("跟随骨骼")) TryFollowBone(boneNameInput, true);
        if (GUILayout.Button("取消标记")) ClearFollowedBone();
        GUILayout.EndHorizontal();

        var suggestions = GetBoneSuggestions();
        if (suggestions.Count > 0)
        {
            GUILayout.Label("常见骨骼点");
            foreach (var name in suggestions.Take(8))
            {
                if (GUILayout.Button(name))
                {
                    boneNameInput = name;
                    TryFollowBone(name, true);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(boneLookupMessage))
        {
            GUILayout.Label(boneLookupMessage, boneLookupMessageIsError ? GUI.skin.box : GUI.skin.label);
        }
        if (followedBone != null)
        {
            GUILayout.Label($"跟随：{followedBoneName}");
            GUILayout.Label($"Spine 局部：X {followedBone.X:F3}  Y {followedBone.Y:F3}");
            GUILayout.Label($"Spine 世界：X {followedBone.WorldX:F3}  Y {followedBone.WorldY:F3}");
            GUILayout.Label($"预览世界：X {followedBoneWorldPosition.x:F3}  Y {followedBoneWorldPosition.y:F3}");
        }
    }

    private List<string> GetBoneSuggestions()
    {
        if (loaded == null) return new List<string>();
        var keywords = new[] { "bullet", "muzzle", "fire", "shoot", "weapon", "attack", "projectile", "emit", "launch", "cast" };
        return loaded.BoneNames
            .Where(name => keywords.Any(keyword => name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    private void LoadResource()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(resourceDirectory)) return;
            var directory = Path.GetFullPath(resourceDirectory);
            var manifest = LoadManifest(directory);
            if (!string.Equals(catalogDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                selectedResourceName = string.Empty;
                resourceEntries = Array.Empty<PreviewResourceEntry>();
            }
            catalogDirectory = directory;
            loadError = string.Empty;
            StartWatcher();
            resourceEntries = PreviewResourceCatalog.Discover(directory, manifest);
            var selected = PreviewResourceCatalog.Select(resourceEntries, selectedResourceName, manifest);
            selectedResourceName = selected?.Name ?? string.Empty;
            RecordPreviewHistory(resourceDirectory);
            if (loaded != null)
            {
                loaded.AnimationState.Event -= OnSpineEvent;
                loaded.Dispose();
                loaded = null;
            }
            followedBone = null;
            if (boneMarker != null) boneMarker.SetActive(false);
            eventLog.Clear();
            if (selected == null) throw new InvalidOperationException("目录内未找到 Spine 骨架资源。");
            if (!string.IsNullOrWhiteSpace(displayMode)) manifest.displayMode = displayMode;
            loaded = PreviewRuntimeLoader.Load(directory, manifest, selected, transform, tintBlack);
            loaded.AnimationState.Event += OnSpineEvent;
            ApplyBoneFollowAfterLoad();
            var animation = loaded.AnimationNames.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(animation)) Play(animation, true);
            Center();
            Debug.Log($"Spine 资源加载成功：{resourceDirectory}；当前 {selectedResourceName}；目录资源 {resourceEntries.Count} 个；动画 {loaded.AnimationNames.Count} 个，骨骼 {loaded.BoneNames.Count} 个。");
        }
        catch (Exception exception)
        {
            loadError = "加载失败：" + exception.Message;
            Debug.LogError("Spine Previewer load failed: " + exception);
        }
    }

    private void Play(string animation, bool loop)
    {
        loaded.AnimationState.SetAnimation(0, animation, loop);
    }

    private void SetSkin(string skin)
    {
        var skeleton = loaded.Renderer != null ? loaded.Renderer.Skeleton : loaded.Graphic.Skeleton;
        skeleton.SetSkin(skin);
        skeleton.SetToSetupPose();
    }

    private void ApplyBoneFollowAfterLoad()
    {
        var resourceIdentity = catalogDirectory + "|" + selectedResourceName;
        if (!string.Equals(boneFollowResourceDirectory, resourceIdentity, StringComparison.OrdinalIgnoreCase))
        {
            boneFollowResourceDirectory = resourceIdentity;
            followDefaultBone = true;
        }

        if (!string.IsNullOrWhiteSpace(followedBoneName))
        {
            TryFollowBone(followedBoneName, false);
            return;
        }

        if (followDefaultBone) TryFollowDefaultBone();
    }

    private void TryFollowDefaultBone()
    {
        boneNameInput = DefaultFollowedBoneName;
        if (loaded.Skeleton.FindBone(DefaultFollowedBoneName) == null) return;
        TryFollowBone(DefaultFollowedBoneName, false);
    }

    private void TryFollowBone(string name, bool reportResult)
    {
        var requestedName = (name ?? string.Empty).Trim();
        if (loaded == null || string.IsNullOrWhiteSpace(requestedName))
        {
            if (reportResult) SetBoneLookupMessage("请输入骨骼名。", true);
            return;
        }

        var bone = loaded.Skeleton.FindBone(requestedName);
        if (bone == null)
        {
            followedBone = null;
            followedBoneName = requestedName;
            if (boneMarker != null) boneMarker.SetActive(false);
            SetBoneLookupMessage($"找不到骨骼：{requestedName}", true);
            return;
        }

        followedBone = bone;
        followedBoneName = bone.Data.Name;
        boneNameInput = bone.Data.Name;
        EnsureBoneMarker();
        boneMarker.SetActive(true);
        SetBoneLookupMessage("标记已跟随。", false);
        UpdateBoneMarker();
    }

    private void ClearFollowedBone()
    {
        followDefaultBone = false;
        followedBone = null;
        followedBoneName = string.Empty;
        boneLookupMessage = string.Empty;
        if (boneMarker != null) boneMarker.SetActive(false);
    }

    private void SetBoneLookupMessage(string message, bool isError)
    {
        boneLookupMessage = message;
        boneLookupMessageIsError = isError;
    }

    private void EnsureBoneMarker()
    {
        if (boneMarker != null) return;
        boneMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        boneMarker.name = "FollowedBoneMarker";
        Destroy(boneMarker.GetComponent<Collider>());
        boneMarker.transform.SetParent(transform, false);
        boneMarkerMaterial = new Material(
            Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Sprites/Default"));
        boneMarkerMaterial.color = new Color(1f, 0.15f, 0.05f, 1f);
        boneMarker.GetComponent<MeshRenderer>().sharedMaterial = boneMarkerMaterial;
    }

    private void UpdateBoneMarker()
    {
        if (loaded == null || followedBone == null || boneMarker == null) return;
        var rendererTransform = loaded.Renderer != null ? loaded.Renderer.transform : loaded.Graphic.transform;
        followedBoneWorldPosition = rendererTransform.TransformPoint(new Vector3(followedBone.WorldX, followedBone.WorldY, 0f));
        boneMarker.transform.position = followedBoneWorldPosition + Vector3.back * 0.08f;
        var markerSize = previewCamera == null ? 0.08f : Mathf.Abs(previewCamera.orthographicSize) * 0.025f;
        if (float.IsNaN(markerSize) || float.IsInfinity(markerSize) || markerSize <= 0f) markerSize = 0.08f;
        boneMarker.transform.localScale = Vector3.one * markerSize;
        boneMarker.SetActive(true);
    }

    private void SetBackground(Color color)
    {
        background = color;
        checkerActive = false;
        if (checkerPlane != null) checkerPlane.SetActive(false);
        previewCamera.backgroundColor = color;
    }

    private void SetCheckerboard()
    {
        if (checkerTexture == null) CreateCheckerboard();
        checkerActive = true;
        previewCamera.backgroundColor = Color.gray;
        if (checkerPlane == null)
        {
            checkerPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            checkerPlane.name = "CheckerboardBackground";
            Destroy(checkerPlane.GetComponent<Collider>());
            checkerPlane.transform.SetParent(transform, false);
            checkerPlane.transform.position = new Vector3(0f, 0f, 5f);
            checkerMaterial = new Material(Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default"));
            checkerMaterial.mainTexture = checkerTexture;
            checkerPlane.GetComponent<MeshRenderer>().sharedMaterial = checkerMaterial;
        }
        checkerPlane.SetActive(true);
        UpdateCheckerPlane();
    }

    private void CreateCheckerboard()
    {
        if (checkerTexture != null) return;
        checkerTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat
        };
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var dark = ((x / 16) + (y / 16)) % 2 == 0;
                checkerTexture.SetPixel(x, y, dark ? new Color(0.72f, 0.72f, 0.72f) : new Color(0.88f, 0.88f, 0.88f));
            }
        }
        checkerTexture.Apply();
    }

    private void UpdateCheckerPlane()
    {
        if (checkerPlane == null || previewCamera == null) return;
        var height = previewCamera.orthographicSize * 2f;
        checkerPlane.transform.localScale = new Vector3(height * previewCamera.aspect, height, 1f);
        checkerPlane.transform.position = new Vector3(previewCamera.transform.position.x, previewCamera.transform.position.y, 5f);
    }

    private void ResetZoom()
    {
        zoom = 1f;
        if (previewCamera == null) return;
        previewCamera.orthographicSize = 5f / zoom;
        UpdateCheckerPlane();
    }

    private void Center()
    {
        if (loaded == null) return;
        loaded.Root.transform.position = Vector3.zero;
        previewCamera.orthographicSize = 5f / zoom;
        UpdateCheckerPlane();
    }

    private void StartWatcher()
    {
        watcher?.Dispose();
        if (string.IsNullOrWhiteSpace(resourceDirectory) || !Directory.Exists(resourceDirectory)) return;
        watcher = new FileSystemWatcher(resourceDirectory)
        {
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        watcher.Changed += (_, _) => reloadRequested = true;
        watcher.Created += (_, _) => reloadRequested = true;
        watcher.Renamed += (_, _) => reloadRequested = true;
        watcher.Deleted += (_, _) => reloadRequested = true;
    }

    private static string PreviewHistoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IdleGearDefence", "SpineTool", "preview-history.json");

    private void LoadPreviewHistory()
    {
        previewHistory.Clear();
        try
        {
            if (!File.Exists(PreviewHistoryPath)) return;
            var data = JsonUtility.FromJson<PreviewHistoryStore>(File.ReadAllText(PreviewHistoryPath));
            if (data?.Items == null) return;
            foreach (var item in data.Items.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Path) && Directory.Exists(x.Path)))
            {
                if (!previewHistory.Any(x => string.Equals(x.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                    previewHistory.Add(item);
            }
            previewHistory.Sort((a, b) => string.CompareOrdinal(b.LastUsedAt, a.LastUsedAt));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("读取预览历史失败：" + exception.Message);
        }
    }

    private void RecordPreviewHistory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return;
        var fullPath = Path.GetFullPath(directory);
        var item = previewHistory.FirstOrDefault(x => string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            item = new PreviewHistoryEntry { Path = fullPath };
            previewHistory.Add(item);
        }
        item.LastUsedAt = DateTime.UtcNow.ToString("O");
        previewHistory.Sort((a, b) => string.CompareOrdinal(b.LastUsedAt, a.LastUsedAt));
        if (previewHistory.Count > 20) previewHistory.RemoveRange(20, previewHistory.Count - 20);
        SavePreviewHistory();
    }

    private void SavePreviewHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreviewHistoryPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(PreviewHistoryPath, JsonUtility.ToJson(new PreviewHistoryStore { Items = previewHistory.ToArray() }, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("保存预览历史失败：" + exception.Message);
        }
    }

    private static PreviewManifest LoadManifest(string directory)
    {
        var path = Path.Combine(directory, "preview-manifest.json");
        return File.Exists(path) ? JsonUtility.FromJson<PreviewManifest>(File.ReadAllText(path)) ?? new PreviewManifest() : new PreviewManifest();
    }

    private static string ReadInputDirectory()
    {
        return ReadInputArgument("--input");
    }

    private static string ReadInputArgument(string name)
    {
        var args = Environment.GetCommandLineArgs();
        for (var i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return string.Empty;
    }

    private void OnDestroy()
    {
        watcher?.Dispose();
        if (loaded != null) loaded.AnimationState.Event -= OnSpineEvent;
        loaded?.Dispose();
        if (checkerTexture != null) Destroy(checkerTexture);
        if (checkerMaterial != null) Destroy(checkerMaterial);
        if (checkerPlane != null) Destroy(checkerPlane);
        if (boneMarkerMaterial != null) Destroy(boneMarkerMaterial);
        if (boneMarker != null) Destroy(boneMarker);
    }

    private void OnSpineEvent(Spine.TrackEntry trackEntry, Spine.Event spineEvent)
    {
        var animationName = trackEntry?.Animation?.Name ?? "";
        var line = $"[{Time.time:F3}] {animationName} / {spineEvent.Data.Name}  int={spineEvent.Int} float={spineEvent.Float:0.###} string={spineEvent.String}";
        eventLog.Enqueue(line);
        while (eventLog.Count > MaxEventLogCount) eventLog.Dequeue();
    }
}

internal static class WindowsFolderPicker
{
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        public string pszDisplayName;
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr memory);

    public static string PickFolder()
    {
        var displayName = new string('\0', 260);
        var info = new BrowseInfo
        {
            hwndOwner = IntPtr.Zero,
            pszDisplayName = displayName,
            lpszTitle = "选择 Spine 资源文件夹",
            ulFlags = 0x0041
        };
        var pidl = SHBrowseForFolder(ref info);
        if (pidl == IntPtr.Zero) return string.Empty;
        try
        {
            var path = new StringBuilder(1024);
            return SHGetPathFromIDList(pidl, path) ? path.ToString() : string.Empty;
        }
        finally
        {
            CoTaskMemFree(pidl);
        }
    }
#else
    public static string PickFolder() => string.Empty;
#endif
}

