using System;
using System.Collections.Generic;
using System.Linq;
using Spine;

public static class PreviewAnimationTimeline
{
    public static IReadOnlyList<Event> ReadEvents(Animation animation)
    {
        if (animation == null) return Array.Empty<Event>();
        return animation.Timelines.Items.Take(animation.Timelines.Count).OfType<EventTimeline>()
            .SelectMany(timeline => timeline.Events).OrderBy(item => item.Time).ToArray();
    }

    // Reset the pose so backward seeks do not retain keys from a later time.
    // Keep the track non-looping while paused, including the exact duration endpoint.
    public static float Seek(Skeleton skeleton, AnimationState state, float seconds)
    {
        var entry = state.GetCurrent(0);
        if (entry == null) return 0f;
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) seconds = 0f;
        var position = Math.Max(0f, Math.Min(seconds, entry.Animation.Duration));
        entry.Loop = false;
        entry.TrackTime = position;
        entry.AnimationLast = position;
        state.TimeScale = 0f;
        skeleton.SetToSetupPose();
        state.Apply(skeleton);
        skeleton.UpdateWorldTransform();
        return position;
    }
}
