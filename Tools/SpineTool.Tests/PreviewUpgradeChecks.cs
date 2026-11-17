using System.IO;
using Spine;

internal static class PreviewUpgradeChecks
{
    public static void Run(string temporaryRoot)
    {
        var root = Path.Combine(temporaryRoot, "recursive");
        WriteResource(root, "hero");
        WriteResource(Path.Combine(root, "A"), "hero");
        WriteResource(Path.Combine(root, "B", "中文技能"), "hero");
        WriteResource(Path.Combine(root, "B", "中文技能"), "bullet");
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        var orphanFolder = Path.Combine(root, "unpaired");
        Directory.CreateDirectory(orphanFolder);
        File.WriteAllText(Path.Combine(orphanFolder, "hero.json"), "{\"bones\":[]}");
        var custom = Path.Combine(root, "custom");
        WriteResource(custom, "effect");
        File.Move(Path.Combine(custom, "effect.atlas.txt"), Path.Combine(custom, "custom.atlas.txt"));
        File.WriteAllText(Path.Combine(custom, "other.atlas"), "");
        var resources = PreviewResourceCatalog.Discover(root, null, directory => directory == custom
            ? new PreviewManifest { skeletonFile = "effect.json", atlasFile = "custom.atlas.txt", alphaWorkflow = "Straight" }
            : new PreviewManifest());
        Check(resources.Count == 6, "Recursive scan lost or duplicated resources.");
        Check(resources.Select(item => item.Key).Distinct().Count() == 6, "Duplicate names from different folders must remain separate.");
        foreach (var item in resources.Where(item => item.Error == ""))
            Check(Path.GetDirectoryName(item.AtlasPath) == item.Directory, "An atlas from a sibling folder was used.");
        Check(resources.Single(item => item.Key == "unpaired/hero").Error != "", "Missing local atlas must be reported.");
        var chosen = PreviewResourceCatalog.Select(resources, "B/中文技能/hero", new PreviewManifest());
        Check(chosen.Key == "B/中文技能/hero" && chosen.SkeletonPaths.Length == 2, "Nested duplicate selection/format grouping failed.");
        var fromManifest = PreviewResourceCatalog.Select(resources, null!, new PreviewManifest { skeletonFile = "B/中文技能/hero.skel.bytes" });
        Check(fromManifest.Key == chosen.Key, "Nested manifest selection failed.");
        var customResource = resources.Single(item => item.Key == "custom/effect");
        Check(Path.GetFileName(customResource.AtlasPath) == "custom.atlas.txt" && customResource.Manifest.alphaWorkflow == "Straight", "Nested manifest was ignored.");
        WriteResource(Path.Combine(root, "new", "deeper"), "added");
        Check(PreviewResourceCatalog.Discover(root).Any(item => item.Key == "new/deeper/added"), "New nested resource not discovered.");
        File.Delete(Path.Combine(root, "A", "hero.json"));
        File.Delete(Path.Combine(root, "A", "hero.skel.bytes"));
        Check(!PreviewResourceCatalog.Discover(root).Any(item => item.Key == "A/hero"), "Deleted nested resource retained.");

        const string json = "{\"skeleton\":{\"hash\":\"synthetic\",\"spine\":\"4.1.24\"},\"bones\":[{\"name\":\"root\"}],\"events\":{\"attack\":{},\"end\":{}},\"animations\":{\"test\":{\"bones\":{\"root\":{\"translate\":[{\"time\":0,\"x\":0},{\"time\":2,\"x\":20}]}},\"events\":[{\"time\":0,\"name\":\"attack\"},{\"time\":0.5,\"name\":\"attack\",\"int\":7,\"float\":1.5,\"string\":\"fire\"},{\"time\":0.5,\"name\":\"attack\"},{\"time\":2,\"name\":\"end\"}]},\"still\":{}}}";
        var data = new SkeletonJson(new AtlasAttachmentLoader(Array.Empty<Atlas>())).ReadSkeletonData(new StringReader(json));
        var skeleton = new Skeleton(data);
        var state = new AnimationState(new AnimationStateData(data));
        var animation = data.FindAnimation("test");
        var markers = PreviewAnimationTimeline.ReadEvents(animation);
        Check(animation.Duration == 2 && markers.Count == 4 && markers[1].Int == 7 && markers[1].String == "fire", "Event time/parameters missing from timeline.");
        state.SetAnimation(0, animation, true);
        var firedEvents = 0;
        state.Event += (_, _) => firedEvents++;
        PreviewAnimationTimeline.Seek(skeleton, state, 1.75f);
        Check(Math.Abs(skeleton.RootBone.X - 17.5f) < 0.001f, "Forward seek pose wrong.");
        PreviewAnimationTimeline.Seek(skeleton, state, 0.25f);
        Check(Math.Abs(skeleton.RootBone.X - 2.5f) < 0.001f, "Backward seek retained future keys.");
        PreviewAnimationTimeline.Seek(skeleton, state, 2);
        Check(state.GetCurrent(0).AnimationTime == 2 && Math.Abs(skeleton.RootBone.X - 20f) < 0.001f, "Endpoint wrapped to start.");
        PreviewAnimationTimeline.Seek(skeleton, state, -1);
        Check(state.GetCurrent(0).TrackTime == 0 && firedEvents == 0, "Seeking fired historical events or did not clamp.");
        state.SetAnimation(0, data.FindAnimation("still"), true);
        PreviewAnimationTimeline.Seek(skeleton, state, 2);
        Check(state.GetCurrent(0).TrackTime == 0 && PreviewAnimationTimeline.ReadEvents(data.FindAnimation("still")).Count == 0, "Zero-duration animation failed.");
        Console.WriteLine("PASS recursive folders, duplicate names, local manifests, event markers and deterministic seeking");
    }

    private static void WriteResource(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), "{\"bones\":[]}");
        File.WriteAllBytes(Path.Combine(directory, name + ".skel.bytes"), new byte[] { 0, 1, 2 });
        File.WriteAllText(Path.Combine(directory, name + ".atlas.txt"), name + ".png\nsize: 2,2\n");
    }

    private static void Check(bool condition, string error)
    { if (!condition) throw new InvalidOperationException(error); }
}
