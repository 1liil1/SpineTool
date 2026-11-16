using System;

[Serializable]
public sealed class PreviewManifest
{
    public string schemaVersion = "1";
    public string resourceName = string.Empty;
    public string skeletonFile = string.Empty;
    public string atlasFile = string.Empty;
    public string dataFormat = "Auto";
    public string displayMode = "SkeletonAnimation";
    public string shader = string.Empty;
    public string alphaWorkflow = "Auto";
}
