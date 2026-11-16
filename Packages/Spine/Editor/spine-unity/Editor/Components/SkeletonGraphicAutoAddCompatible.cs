using UnityEngine;
using UnityEditor;
using Spine.Unity;

namespace Spine.Unity.Editor
{
    /// <summary>
    /// SkeletonGraphic 自动添加 Compatible 组件的编辑器扩展
    /// </summary>
    [InitializeOnLoad]
    public static class SkeletonGraphicAutoAddCompatible
    {
        static SkeletonGraphicAutoAddCompatible()
        {
            // 监听组件添加事件
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        private static void OnComponentAdded(Component component)
        {

            // 检查是否是 SkeletonGraphic 组件
            if (component is SkeletonGraphic skeletonGraphic)
            {
                // 检查是否已经有 SkeletonGraphicCompatible 组件
                if (skeletonGraphic.GetComponent<SkeletonGraphicCompatible>() == null)
                {
                    // 添加 SkeletonGraphicCompatible 组件
                    skeletonGraphic.gameObject.AddComponent<SkeletonGraphicCompatible>();

                    // 标记为已修改，以便保存
                    EditorUtility.SetDirty(skeletonGraphic.gameObject);

                    Debug.Log($"自动为 {skeletonGraphic.gameObject.name} 添加了 SkeletonGraphicCompatible 组件");
                }
            }
        }
    }
}
