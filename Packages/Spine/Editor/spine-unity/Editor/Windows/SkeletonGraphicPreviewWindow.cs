/******************************************************************************
 * SkeletonGraphic 预览编辑器面板：打开时自动查找并驱动场景中所有 SkeletonGraphic 播放，关闭后功能消失，对场景/预设零侵入。
 *****************************************************************************/

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spine.Unity.Editor {

	public class SkeletonGraphicPreviewWindow : EditorWindow {

		List<SkeletonGraphic> _graphics = new List<SkeletonGraphic>();
		double _lastEditorTime;
		bool _listDirty = true;

		[MenuItem("Window/Spine/SkeletonGraphic Preview", false, 2050)]
		public static void Open () {
			SkeletonGraphicPreviewWindow w = GetWindow<SkeletonGraphicPreviewWindow>(utility: true);
			w.titleContent = new GUIContent("SkeletonGraphic Preview");
			w.minSize = new Vector2(260f, 80f);
		}

		void OnEnable () {
			_lastEditorTime = EditorApplication.timeSinceStartup;
			EditorApplication.update += OnEditorUpdate;
			EditorApplication.hierarchyChanged += OnHierarchyChanged;
			EditorSceneManager.sceneOpened += OnSceneOpened;
			PrefabStage.prefabStageOpened += OnPrefabStageChanged;
			PrefabStage.prefabStageClosing += OnPrefabStageChanged;
			RefreshGraphicsList();
		}

		void OnDisable () {
			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.hierarchyChanged -= OnHierarchyChanged;
			EditorSceneManager.sceneOpened -= OnSceneOpened;
			PrefabStage.prefabStageOpened -= OnPrefabStageChanged;
			PrefabStage.prefabStageClosing -= OnPrefabStageChanged;
			_graphics.Clear();
		}

		void OnHierarchyChanged () { _listDirty = true; }
		void OnSceneOpened (UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode) { _listDirty = true; }
		void OnPrefabStageChanged (PrefabStage stage) { _listDirty = true; }

		void RefreshGraphicsList () {
			_graphics.Clear();
			if (Application.isPlaying) return;
			var seen = new HashSet<SkeletonGraphic>();

			// 场景中的 SkeletonGraphic
			SkeletonGraphic[] all = Object.FindObjectsOfType<SkeletonGraphic>();
			for (int i = 0; i < all.Length; i++) {
				SkeletonGraphic g = all[i];
				if (g == null) continue;
				if (g.gameObject.scene.IsValid() && seen.Add(g)) _graphics.Add(g);
			}

			// 当前打开的预制体中的 SkeletonGraphic
			PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
			if (stage != null && stage.scene.isLoaded && stage.prefabContentsRoot != null) {
				SkeletonGraphic[] inPrefab = stage.prefabContentsRoot.GetComponentsInChildren<SkeletonGraphic>(true);
				for (int i = 0; i < inPrefab.Length; i++) {
					SkeletonGraphic g = inPrefab[i];
					if (g == null) continue;
					if (seen.Add(g)) _graphics.Add(g);
				}
			}

			_listDirty = false;
		}

		void OnEditorUpdate () {
			if (Application.isPlaying) return;
			if (_listDirty) RefreshGraphicsList();
			if (_graphics.Count == 0) return;

			double now = EditorApplication.timeSinceStartup;
			float deltaTime = (float)(now - _lastEditorTime);
			_lastEditorTime = now;
			deltaTime = Mathf.Clamp(deltaTime, 0.0001f, 0.05f);

			for (int i = _graphics.Count - 1; i >= 0; i--) {
				SkeletonGraphic g = _graphics[i];
				if (g == null) { _graphics.RemoveAt(i); continue; }
				if (!g.IsValid) continue;
				if (g.GetComponent<SkeletonGraphicEditorPreview>() != null) continue;

				g.Update(deltaTime);

				Spine.TrackEntry current = g.AnimationState.GetCurrent(0);
				if (current != null && current.IsComplete) {
					string animName = g.startingAnimation;
					if (!string.IsNullOrEmpty(animName)) {
						Spine.Animation anim = g.Skeleton.Data.FindAnimation(animName);
						if (anim != null) g.AnimationState.SetAnimation(0, anim, g.startingLoop);
					}
				}

				g.LateUpdate();
			}

#if UNITY_2017_2_OR_NEWER
			EditorApplication.QueuePlayerLoopUpdate();
#else
			SceneView.RepaintAll();
#endif
		}

		void OnGUI () {
			if (_listDirty) RefreshGraphicsList();
			EditorGUILayout.Space(6);
			EditorGUILayout.LabelField("SkeletonGraphic 预览", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(
				"本窗口打开时，将自动查找当前已打开场景以及当前打开的预制体中的全部 SkeletonGraphic 并在 Scene 视图中播放动画。\n关闭窗口后预览停止，不向场景或预设添加任何组件。",
				MessageType.Info);
			EditorGUILayout.Space(4);
			EditorGUILayout.LabelField("当前数量: " + _graphics.Count, EditorStyles.miniLabel);
			if (GUILayout.Button("刷新查找")) _listDirty = true;
		}
	}
}
