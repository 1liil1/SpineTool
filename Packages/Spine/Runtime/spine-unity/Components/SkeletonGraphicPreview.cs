using UnityEngine;
using UnityEngine.Scripting;
using UnityEditor;
namespace Spine.Unity
{
#if UNITY_EDITOR

    [ExecuteAlways]
    [RequireComponent(typeof(SkeletonGraphic)), Preserve]
    public class SkeletonGraphicEditorPreview : MonoBehaviour
    {
        SkeletonGraphic graphic;
        double lastEditorTime;

        void OnEnable()
        {
            graphic = GetComponent<SkeletonGraphic>();
            lastEditorTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += EditorUpdate;
        }

        void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
        }

        void OnDestroy()
        {
            // 用户手动删除本组件后，不再自动添加
            if (graphic != null)
            {
                graphic.SetPreviewRemovedByUser();
                EditorUtility.SetDirty(graphic);
            }
        }

        void EditorUpdate()
        {
            if (Application.isPlaying) return;
            if (graphic == null || !graphic.IsValid) return;

            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(now - lastEditorTime);
            lastEditorTime = now;
            // 限制 delta，避免编辑器卡顿后一次跳太多或 delta 为 0 导致不动
            deltaTime = Mathf.Clamp(deltaTime, 0.0001f, 0.05f);

            graphic.Update(deltaTime);

            // 播完后无论是否勾选循环，都从头开始播
            Spine.TrackEntry current = graphic.AnimationState.GetCurrent(0);
            if (current != null && current.IsComplete) {
                string animName = graphic.startingAnimation;
                if (!string.IsNullOrEmpty(animName)) {
                    Spine.Animation anim = graphic.Skeleton.Data.FindAnimation(animName);
                    if (anim != null)
                        graphic.AnimationState.SetAnimation(0, anim, graphic.startingLoop);
                }
            }

            graphic.LateUpdate();

#if UNITY_2017_2_OR_NEWER
            EditorApplication.QueuePlayerLoopUpdate();
#else
            SceneView.RepaintAll();
#endif
        }
    }
#endif
}
