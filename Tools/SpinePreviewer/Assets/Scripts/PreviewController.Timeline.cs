using System;
using UnityEngine;

public sealed partial class PreviewController
{
    private void UpdateUiRects()
    {
        var menuWidth = Mathf.Min(380, Screen.width * 0.38f);
        controlRect = new Rect(12, 12, menuWidth, Screen.height - 24);
        timelineRect = new Rect(menuWidth + 28, Screen.height - 200, Mathf.Max(200, Screen.width - menuWidth - 40), 188);
    }

    private void RequestReload()
    {
        lastResourceChangeUtc = DateTime.UtcNow;
        reloadRequested = true;
    }

    private void ResetTimeline()
    {
        if (Event.current != null && scrubControl != 0 && GUIUtility.hotControl == scrubControl) GUIUtility.hotControl = 0;
        scrubbing = false;
        scrubControl = 0;
        selectedEvent = -1;
        animationEvents = Array.Empty<Spine.Event>();
        draggingObject = false;
    }

    private void TogglePlayback()
    {
        playing = !playing;
        var track = loaded?.AnimationState.GetCurrent(0);
        if (track == null) return;
        if (playing)
        {
            if (track.TrackTime >= track.Animation.Duration) track.TrackTime = 0;
            track.Loop = true;
            track.AnimationLast = track.AnimationTime;
        }
        loaded.AnimationState.TimeScale = playing ? speed : 0f;
    }

    private void SeekTo(float seconds)
    {
        if (loaded == null) return;
        playing = false;
        PreviewAnimationTimeline.Seek(loaded.Skeleton, loaded.AnimationState, seconds);
        // OnGUI runs after normal animation updates. Refresh the mesh immediately while scrubbing.
        if (loaded.Renderer != null) loaded.Renderer.LateUpdate();
        else loaded.Graphic.UpdateMesh();
        UpdateBoneMarker();
    }

    private void DrawTimeline()
    {
        if (loaded == null) return;
        var track = loaded.AnimationState.GetCurrent(0);
        GUI.BeginGroup(timelineRect, GUI.skin.box);
        var width = timelineRect.width;
        if (track == null)
        {
            GUI.Label(new Rect(16, 14, width - 32, 40), "此资源没有动画", timelineLabel);
            GUI.EndGroup();
            return;
        }

        var duration = track.Animation.Duration;
        var time = Mathf.Clamp(track.AnimationTime, 0, duration);
        if (GUI.Button(new Rect(14, 10, 62, 28), playing ? "暂停" : "播放")) TogglePlayback();
        GUI.Label(new Rect(88, 10, width - 300, 28), track.Animation.Name, timelineLabel);
        GUI.Label(new Rect(width - 224, 10, 210, 28), $"{time:0.000} / {duration:0.000} 秒   {(duration > 0 ? time / duration : 0):P0}", timelineLabel);

        var bar = new Rect(22, 50, width - 44, 16);
        DrawTimelineRect(bar, new Color(0.16f, 0.18f, 0.22f));
        if (duration > 0)
            DrawTimelineRect(new Rect(bar.x, bar.y, bar.width * time / duration, bar.height), new Color(0.16f, 0.56f, 0.95f));
        for (var index = 0; index <= 4; index++)
        {
            var x = bar.x + bar.width * index / 4f;
            DrawTimelineRect(new Rect(x, bar.y, 1, bar.height), new Color(0.7f, 0.72f, 0.75f));
            GUI.Label(new Rect(Mathf.Clamp(x - 20, 8, width - 66), 68, 60, 18), $"{duration * index / 4f:0.##}s", eventLabel);
        }
        var playhead = bar.x + (duration > 0 ? bar.width * time / duration : 0);
        DrawTimelineRect(new Rect(playhead - 2, bar.y - 5, 4, bar.height + 10), Color.white);
        HandleTimelineInput(bar, duration);

        // All markers remain clickable. Names use three collision-avoiding lanes.
        var laneEnds = new[] { -100f, -100f, -100f };
        var hoveredEvent = -1;
        for (var index = 0; index < animationEvents.Count; index++)
        {
            var item = animationEvents[index];
            var x = bar.x + (duration > 0 ? Mathf.Clamp01(item.Time / duration) * bar.width : 0);
            var highlight = selectedEvent == index || Mathf.Abs(time - item.Time) < 0.045f;
            var labelText = $"{item.Data.Name}  {item.Time:0.###}s";
            var labelWidth = Mathf.Min(eventLabel.CalcSize(new GUIContent(labelText)).x + 8, Mathf.Min(240, width - 44));
            var labelX = Mathf.Clamp(x + 9, 16, width - labelWidth - 12);
            var eventLane = -1;
            for (var lane = 0; lane < laneEnds.Length; lane++)
                if (labelX >= laneEnds[lane] + 7) { eventLane = lane; break; }
            var markerRect = new Rect(x - 7, 87 + Mathf.Max(0, eventLane) * 17, 15, 21);
            DrawTimelineRect(new Rect(x, bar.y - 3, 2, bar.height + 6), highlight ? Color.yellow : new Color(1f, 0.6f, 0.2f));
            var oldColor = GUI.color;
            GUI.color = highlight ? Color.yellow : new Color(1f, 0.7f, 0.35f);
            if (GUI.Button(markerRect, new GUIContent("◆", EventDescription(item)), eventMarker))
            {
                selectedEvent = index;
                SeekTo(item.Time);
            }
            GUI.color = oldColor;
            if (markerRect.Contains(Event.current.mousePosition)) hoveredEvent = index;
            if (eventLane >= 0)
            {
                var labelRect = new Rect(labelX, 89 + eventLane * 17, labelWidth, 19);
                GUI.Label(labelRect, new GUIContent(labelText, EventDescription(item)), eventLabel);
                if (labelRect.Contains(Event.current.mousePosition)) hoveredEvent = index;
                laneEnds[eventLane] = labelX + labelWidth;
            }
        }
        if (animationEvents.Count == 0) GUI.Label(new Rect(18, 90, width - 36, 24), "此动画没有事件", eventLabel);
        var detailIndex = hoveredEvent >= 0 ? hoveredEvent : selectedEvent;
        GUI.Label(new Rect(16, 145, width - 32, 23), detailIndex >= 0 && detailIndex < animationEvents.Count
            ? EventDescription(animationEvents[detailIndex]) : $"事件 {animationEvents.Count} 个 · 点击 ◆ 定位，悬停查看参数", eventLabel);
        GUI.Label(new Rect(16, 166, width - 32, 20), "拖动进度条定位并暂停，点击播放继续循环", eventLabel);
        GUI.EndGroup();
    }

    private void HandleTimelineInput(Rect bar, float duration)
    {
        var control = GUIUtility.GetControlID("SpineAnimationTimeline".GetHashCode(), FocusType.Passive);
        var current = Event.current;
        var hit = new Rect(bar.x - 8, bar.y - 10, bar.width + 16, bar.height + 20);
        switch (current.GetTypeForControl(control))
        {
            case EventType.MouseDown:
                if (current.button != 0 || !hit.Contains(current.mousePosition) || duration <= 0) return;
                GUIUtility.hotControl = control;
                scrubControl = control;
                scrubbing = true;
                selectedEvent = -1;
                SeekTo(Mathf.Clamp01((current.mousePosition.x - bar.x) / bar.width) * duration);
                current.Use();
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl != control) return;
                SeekTo(Mathf.Clamp01((current.mousePosition.x - bar.x) / bar.width) * duration);
                current.Use();
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl != control || current.button != 0) return;
                SeekTo(Mathf.Clamp01((current.mousePosition.x - bar.x) / bar.width) * duration);
                GUIUtility.hotControl = 0;
                scrubbing = false;
                scrubControl = 0;
                current.Use();
                break;
        }
    }

    private static void DrawTimelineRect(Rect rect, Color color)
    {
        var previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static string EventDescription(Spine.Event item)
        => $"{item.Data.Name} @ {item.Time:0.###}s    int={item.Int}   float={item.Float:0.###}   string={item.String}";
}
