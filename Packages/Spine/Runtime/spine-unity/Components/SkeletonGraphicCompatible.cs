using UnityEngine;
using UnityEngine.UI;
using Spine.Unity;
using UnityEngine.Scripting;

namespace Spine.Unity
{
    /// <summary>
    /// SkeletonGraphic 兼容组件，用于同步 SkeletonGraphic 的 alpha 值与 CanvasGroup 的 alpha 值
    /// </summary>
    [RequireComponent(typeof(SkeletonGraphic)), Preserve]
    public class SkeletonGraphicCompatible : MonoBehaviour
    {
        [Header("组件引用")]
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private SkeletonGraphic skeletonGraphic;

        [Header("设置")]
        [SerializeField] private bool autoFindCanvasGroup = true;
        [Tooltip("启用后同步 CanvasGroup.alpha 到材质 Tint Color（_Color）的透明度，而非 SkeletonGraphic.color")]
        [SerializeField] private bool isTint;

        private static readonly int TintColorPropertyId = Shader.PropertyToID("_Color");

        private float lastCanvasGroupAlpha = 1f;

        private void Start()
        {
            // 获取 SkeletonGraphic 组件
            if (skeletonGraphic == null)
            {
                skeletonGraphic = GetComponent<SkeletonGraphic>();
            }

            // 自动查找 CanvasGroup
            if (autoFindCanvasGroup && canvasGroup == null)
            {
                canvasGroup = GetComponentInParent<CanvasGroup>();
            }
        }

        private void Update()
        {
            // 自动查找 CanvasGroup
            if (autoFindCanvasGroup && canvasGroup == null)
            {
                canvasGroup = GetComponentInParent<CanvasGroup>();
            }

            // 检查组件是否有效
            if (skeletonGraphic == null || canvasGroup == null)
                return;

            // 检查 CanvasGroup 的 alpha 值是否发生变化
            if (Mathf.Abs(canvasGroup.alpha - lastCanvasGroupAlpha) > 0.001f)
            {
                UpdateSkeletonGraphicAlpha();
                lastCanvasGroupAlpha = canvasGroup.alpha;
            }
        }

        /// <summary>
        /// 更新 SkeletonGraphic 的 alpha 值
        /// </summary>
        private void UpdateSkeletonGraphicAlpha()
        {
            if (skeletonGraphic == null || canvasGroup == null)
                return;

            if (isTint)
            {
                Material material = skeletonGraphic.material;
                if (material == null || !material.HasProperty(TintColorPropertyId))
                    return;

                Color tintColor = material.GetColor(TintColorPropertyId);
                tintColor.a = canvasGroup.alpha;
                material.SetColor(TintColorPropertyId, tintColor);
            }
            else
            {
                Color currentColor = skeletonGraphic.color;
                currentColor.a = canvasGroup.alpha;
                skeletonGraphic.color = currentColor;
            }
        }

        /// <summary>
        /// 手动设置 CanvasGroup 引用
        /// </summary>
        /// <param name="newCanvasGroup">新的 CanvasGroup 引用</param>
        public void SetCanvasGroup(CanvasGroup newCanvasGroup)
        {
            canvasGroup = newCanvasGroup;
            if (canvasGroup != null)
            {
                UpdateSkeletonGraphicAlpha();
                lastCanvasGroupAlpha = canvasGroup.alpha;
            }
        }

        /// <summary>
        /// 手动设置 SkeletonGraphic 引用
        /// </summary>
        /// <param name="newSkeletonGraphic">新的 SkeletonGraphic 引用</param>
        public void SetSkeletonGraphic(SkeletonGraphic newSkeletonGraphic)
        {
            skeletonGraphic = newSkeletonGraphic;
            if (skeletonGraphic != null && canvasGroup != null)
            {
                UpdateSkeletonGraphicAlpha();
            }
        }

        /// <summary>
        /// 强制同步 alpha 值
        /// </summary>
        public void ForceSync()
        {
            UpdateSkeletonGraphicAlpha();
            if (canvasGroup != null)
            {
                lastCanvasGroupAlpha = canvasGroup.alpha;
            }
        }

        private void OnValidate()
        {
            // 编辑器中自动查找组件
            if (skeletonGraphic == null)
            {
                skeletonGraphic = GetComponent<SkeletonGraphic>();
            }

            if (autoFindCanvasGroup && canvasGroup == null)
            {
                canvasGroup = GetComponentInParent<CanvasGroup>();
            }
        }
    }
}
