using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Spine;
using Spine.Unity;
using UnityEngine;

public sealed class PreviewLoadedAsset : IDisposable
{
    public GameObject Root { get; }
    public SkeletonDataAsset SkeletonDataAsset { get; }
    public Spine.SkeletonData Data { get; }
    public SpineAtlasAsset AtlasAsset { get; }
    public SkeletonRenderer Renderer { get; }
    public SkeletonGraphic Graphic { get; }
    public Spine.AnimationState AnimationState => Renderer is SkeletonAnimation animation
        ? animation.AnimationState
        : Graphic.AnimationState;

    public Spine.Skeleton Skeleton => Renderer != null ? Renderer.Skeleton : Graphic.Skeleton;

    public IReadOnlyList<string> AnimationNames
    {
        get
        {
            return Data.Animations.Items.Take(Data.Animations.Count).Select(x => x.Name).ToArray();
        }
    }

    public IReadOnlyList<string> SkinNames
    {
        get
        {
            return Data.Skins.Items.Take(Data.Skins.Count).Select(x => x.Name).ToArray();
        }
    }

    public IReadOnlyList<string> BoneNames
    {
        get
        {
            return Data.Bones.Items.Take(Data.Bones.Count).Select(x => x.Name).ToArray();
        }
    }

    public PreviewLoadedAsset(GameObject root, SkeletonDataAsset skeletonDataAsset, Spine.SkeletonData data, SpineAtlasAsset atlasAsset, SkeletonRenderer renderer, SkeletonGraphic graphic)
    {
        Root = root;
        SkeletonDataAsset = skeletonDataAsset;
        Data = data;
        AtlasAsset = atlasAsset;
        Renderer = renderer;
        Graphic = graphic;
    }

    public void Dispose()
    {
        if (Root != null) UnityEngine.Object.Destroy(Root);
        if (SkeletonDataAsset != null)
        {
            if (SkeletonDataAsset.skeletonJSON != null) UnityEngine.Object.Destroy(SkeletonDataAsset.skeletonJSON);
            UnityEngine.Object.Destroy(SkeletonDataAsset);
        }
        if (AtlasAsset != null)
        {
            var materials = AtlasAsset.Materials.Where(x => x != null).Distinct().ToArray();
            foreach (var texture in materials.Select(x => x.mainTexture).Where(x => x != null).Distinct()) UnityEngine.Object.Destroy(texture);
            foreach (var material in materials) UnityEngine.Object.Destroy(material);
            if (AtlasAsset.atlasFile != null) UnityEngine.Object.Destroy(AtlasAsset.atlasFile);
            UnityEngine.Object.Destroy(AtlasAsset);
        }
    }

    public void SetTintBlack(bool enabled)
    {
        if (Renderer != null) Renderer.tintBlack = enabled;
        if (Graphic != null)
        {
            var settings = Graphic.MeshGenerator.settings;
            settings.tintBlack = enabled;
            Graphic.MeshGenerator.settings = settings;
            if (Graphic.IsValid) Graphic.UpdateMesh();
        }

        if (AtlasAsset == null) return;
        foreach (var material in AtlasAsset.Materials)
            PreviewRuntimeLoader.ApplyTintBlack(material, enabled);
    }
}

public static class PreviewRuntimeLoader
{
    public static PreviewLoadedAsset Load(string directory, PreviewManifest manifest, Transform parent, bool tintBlack = true)
    {
        var resource = PreviewResourceCatalog.Select(PreviewResourceCatalog.Discover(directory, manifest), null, manifest);
        if (resource == null) throw new InvalidOperationException("未找到 JSON 或 SKEL 骨架数据。");
        return Load(directory, manifest, resource, parent, tintBlack);
    }

    public static PreviewLoadedAsset Load(string directory, PreviewManifest manifest, PreviewResourceEntry resource, Transform parent, bool tintBlack = true)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("资源目录不存在：" + directory);

        if (!string.IsNullOrEmpty(resource.Error)) throw new InvalidOperationException(resource.Error);
        var atlasPath = resource.AtlasPath;

        var atlasText = File.ReadAllText(atlasPath);
        var pma = ResolvePma(manifest, atlasText);
        var textureFiles = ReadAtlasPageNames(atlasText)
            .Select(name => Path.Combine(Path.GetDirectoryName(atlasPath) ?? directory, name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var textures = new List<Texture2D>();
        SpineAtlasAsset atlasAsset = null;
        SkeletonDataAsset skeletonDataAsset = null;
        TextAsset atlasTextAsset = null;
        GameObject root = null;
        try
        {
            foreach (var path in textureFiles) textures.Add(LoadTexture(path, pma));
            var useGraphic = string.Equals(manifest.displayMode, "SkeletonGraphic", StringComparison.OrdinalIgnoreCase);
            var shader = ResolveShader(manifest.shader, useGraphic, tintBlack);
            if (shader == null) throw new InvalidOperationException("找不到 Spine Shader：" + (manifest.shader ?? string.Empty));

            for (var i = 0; i < textures.Count; i++)
                textures[i].name = Path.GetFileNameWithoutExtension(textureFiles[i]);

            atlasTextAsset = new TextAsset(atlasText);
            atlasAsset = SpineAtlasAsset.CreateRuntimeInstance(atlasTextAsset, textures.ToArray(), shader, true);
            foreach (var material in atlasAsset.Materials)
            {
                if (material == null) continue;
                if (material.HasProperty("_StraightAlphaInput"))
                    material.SetFloat("_StraightAlphaInput", pma ? 0f : 1f);
                ApplyTintBlack(material, tintBlack);
            }
            var explicitSkeleton = string.IsNullOrWhiteSpace(manifest.skeletonFile) ? null : Path.GetFullPath(Path.Combine(directory, manifest.skeletonFile));
            var skeletonPaths = resource.SkeletonPaths.OrderBy(path => string.Equals(path, explicitSkeleton, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToArray();
            if (skeletonPaths.Length == 0) throw new InvalidOperationException("未找到 JSON 或 SKEL 骨架数据。");
            Spine.SkeletonData skeletonData = null;
            Exception lastSkeletonError = null;
            foreach (var skeletonPath in skeletonPaths)
            {
                try
                {
                    var created = CreateSkeletonDataAsset(skeletonPath, atlasAsset);
                    skeletonDataAsset = created.Asset;
                    skeletonData = created.Data;
                    break;
                }
                catch (Exception exception)
                {
                    lastSkeletonError = exception;
                    Debug.LogWarning("Spine 数据加载失败，尝试备用格式：" + skeletonPath + "；" + exception.Message);
                }
            }
            if (skeletonDataAsset == null)
                throw new InvalidOperationException("JSON 和 SKEL 都无法由当前 Spine Runtime 加载。", lastSkeletonError);
            root = new GameObject("SpinePreview_" + resource.Name);
            root.transform.SetParent(parent, false);
            if (useGraphic)
            {
                var graphicObject = new GameObject("SkeletonGraphic", typeof(RectTransform), typeof(CanvasRenderer));
                graphicObject.transform.SetParent(root.transform, false);
                var graphic = graphicObject.AddComponent<SkeletonGraphic>();
                graphic.material = atlasAsset.PrimaryMaterial;
                graphic.skeletonDataAsset = skeletonDataAsset;
                graphic.Initialize(true);
                var graphicSettings = graphic.MeshGenerator.settings;
                graphicSettings.pmaVertexColors = pma;
                graphicSettings.tintBlack = tintBlack;
                graphic.MeshGenerator.settings = graphicSettings;
                graphic.UpdateMesh();
                return new PreviewLoadedAsset(root, skeletonDataAsset, skeletonData, atlasAsset, null, graphic);
            }

            var renderer = root.AddComponent<SkeletonAnimation>();
            renderer.skeletonDataAsset = skeletonDataAsset;
            renderer.pmaVertexColors = pma;
            renderer.tintBlack = tintBlack;
            renderer.Initialize(true);
            return new PreviewLoadedAsset(root, skeletonDataAsset, skeletonData, atlasAsset, renderer, null);
        }
        catch
        {
            new PreviewLoadedAsset(root, skeletonDataAsset, null, atlasAsset, null, null).Dispose();
            if (atlasAsset == null)
            {
                foreach (var texture in textures) if (texture != null) UnityEngine.Object.Destroy(texture);
                if (atlasTextAsset != null) UnityEngine.Object.Destroy(atlasTextAsset);
            }
            throw;
        }
    }

    internal static void ApplyTintBlack(Material material, bool enabled)
    {
        if (material == null || material.shader == null) return;
        if (material.HasProperty("_TintBlack"))
        {
            material.SetFloat("_TintBlack", enabled ? 1f : 0f);
            if (enabled) material.EnableKeyword("_TINT_BLACK_ON");
            else material.DisableKeyword("_TINT_BLACK_ON");
            return;
        }

        var targetName = TintShaderName(material.shader.name, enabled);
        if (targetName == null || targetName == material.shader.name) return;
        var shader = FindShader(targetName);
        if (shader == null) return;
        var straightAlpha = material.HasProperty("_StraightAlphaInput") && material.GetFloat("_StraightAlphaInput") > 0.5f;
        material.shader = shader;
        if (!material.HasProperty("_StraightAlphaInput")) return;
        material.SetFloat("_StraightAlphaInput", straightAlpha ? 1f : 0f);
        if (straightAlpha) material.EnableKeyword("_STRAIGHT_ALPHA_INPUT");
        else material.DisableKeyword("_STRAIGHT_ALPHA_INPUT");
    }

    private static Shader ResolveShader(string manifestShader, bool useGraphic, bool tintBlack)
    {
        if (!string.IsNullOrWhiteSpace(manifestShader))
        {
            var explicitShader = Shader.Find(manifestShader);
            if (explicitShader != null) return explicitShader;
        }

        var shaderName = useGraphic
            ? (tintBlack ? GraphicTintShaderName : GraphicShaderName)
            : (tintBlack ? SkeletonTintShaderName : SkeletonShaderName);
        return FindShader(shaderName) ?? FindShader(SkeletonShaderName);
    }

    private static Shader FindShader(string shaderName)
    {
        var resourceName = ShaderResourceName(shaderName);
        if (!string.IsNullOrEmpty(resourceName))
        {
            var template = Resources.Load<Material>(resourceName);
            if (template != null && template.shader != null) return template.shader;
        }

        return Shader.Find(shaderName);
    }

    private static string ShaderResourceName(string shaderName)
    {
        if (shaderName == SkeletonShaderName) return "SpineSkeletonPreview";
        if (shaderName == SkeletonTintShaderName) return "SpineSkeletonTintBlackPreview";
        if (shaderName == GraphicShaderName) return "SpineSkeletonGraphicPreview";
        if (shaderName == GraphicTintShaderName) return "SpineSkeletonGraphicTintBlackPreview";
        return null;
    }

    private static string TintShaderName(string currentName, bool enabled)
    {
        if (currentName == SkeletonShaderName || currentName == SkeletonTintShaderName)
            return enabled ? SkeletonTintShaderName : SkeletonShaderName;
        if (currentName == GraphicShaderName || currentName == GraphicTintShaderName)
            return enabled ? GraphicTintShaderName : GraphicShaderName;
        return null;
    }

    private const string SkeletonShaderName = "Spine/Skeleton";
    private const string SkeletonTintShaderName = "Spine/Skeleton Tint Black";
    private const string GraphicShaderName = "Spine/SkeletonGraphic";
    private const string GraphicTintShaderName = "Spine/SkeletonGraphic Tint Black";

    private static (SkeletonDataAsset Asset, Spine.SkeletonData Data) CreateSkeletonDataAsset(string path, SpineAtlasAsset atlasAsset)
    {
        var asset = ScriptableObject.CreateInstance<SkeletonDataAsset>();
        try
        {
            asset.atlasAssets = new AtlasAssetBase[] { atlasAsset };
            asset.scale = 0.01f;
            var bytes = File.ReadAllBytes(path);
            if (LooksLikeJson(bytes))
            {
                asset.skeletonJSON = new TextAsset(File.ReadAllText(path));
                var jsonData = asset.GetSkeletonData(true);
                if (jsonData == null) throw new InvalidDataException("JSON 数据无法解析。");
                return (asset, jsonData);
            }

            var loader = new AtlasAttachmentLoader(atlasAsset.GetAtlas());
            var binary = new SkeletonBinary(loader) { Scale = asset.scale };
            using var stream = new MemoryStream(bytes);
            var data = binary.ReadSkeletonData(stream);
            if (data == null)
                throw new InvalidDataException("SKEL 数据无法由当前 Spine Runtime 解析，可能是 Spine major/minor 版本不兼容。");
            var initializeMethod = typeof(SkeletonDataAsset).GetMethod("InitializeWithData", BindingFlags.Instance | BindingFlags.NonPublic);
            if (initializeMethod == null)
                throw new MissingMethodException("当前 Spine Runtime 没有 SkeletonDataAsset.InitializeWithData。");
            initializeMethod.Invoke(asset, new object[] { data });
            // GetSkeletonData first checks skeletonJSON for null even when the runtime cache is initialized.
            // A placeholder TextAsset keeps SkeletonAnimation/SkeletonGraphic initialization on the cached binary data.
            asset.skeletonJSON = new TextAsset("{}");
            return (asset, data);
        }
        catch
        {
            if (asset.skeletonJSON != null) UnityEngine.Object.Destroy(asset.skeletonJSON);
            UnityEngine.Object.Destroy(asset);
            throw;
        }
    }

    private static Texture2D LoadTexture(string path, bool pma)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Atlas 页面纹理不存在", path);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, pma);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
        {
            UnityEngine.Object.Destroy(texture);
            throw new InvalidDataException("PNG 无法解码：" + path);
        }
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static IEnumerable<string> ReadAtlasPageNames(string atlasText)
    {
        using var reader = new StringReader(atlasText.Replace("\r", string.Empty));
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var next = reader.ReadLine();
            if (next == null) yield break;
            if (next.TrimStart().StartsWith("size:", StringComparison.OrdinalIgnoreCase))
                yield return line.Trim();
        }
    }

    private static bool LooksLikeJson(byte[] bytes)
    {
        var length = Mathf.Min(bytes.Length, 256);
        var index = 0;
        if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) index = 3;
        while (index < length && char.IsWhiteSpace((char)bytes[index])) index++;
        return index < length && (bytes[index] == '{' || bytes[index] == '[');
    }

    private static bool ResolvePma(PreviewManifest manifest, string atlasText)
    {
        if (string.Equals(manifest.alphaWorkflow, "PMA", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(manifest.alphaWorkflow, "Straight", StringComparison.OrdinalIgnoreCase)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(atlasText, @"(?im)^\s*pma\s*:\s*true\s*$");
    }
}
