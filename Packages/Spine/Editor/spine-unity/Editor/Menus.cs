/******************************************************************************
 * Spine Runtimes License Agreement
 * Last updated July 28, 2023. Replaces all prior versions.
 *
 * Copyright (c) 2013-2023, Esoteric Software LLC
 *
 * Integration of the Spine Runtimes into software or otherwise creating
 * derivative works of the Spine Runtimes is permitted under the terms and
 * conditions of Section 2 of the Spine Editor License Agreement:
 * http://esotericsoftware.com/spine-editor-license
 *
 * Otherwise, it is permitted to integrate the Spine Runtimes into software or
 * otherwise create derivative works of the Spine Runtimes (collectively,
 * "Products"), provided that each user of the Products must obtain their own
 * Spine Editor license and redistribution of the Products in any form must
 * include this license and copyright notice.
 *
 * THE SPINE RUNTIMES ARE PROVIDED BY ESOTERIC SOFTWARE LLC "AS IS" AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL ESOTERIC SOFTWARE LLC BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES,
 * BUSINESS INTERRUPTION, OR LOSS OF USE, DATA, OR PROFITS) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THE
 * SPINE RUNTIMES, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 *****************************************************************************/

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Spine.Unity.Editor {
	public static class Menus {
		[MenuItem("GameObject/Spine/SkeletonRenderer", false, 10)]
		static public void CreateSkeletonRendererGameObject () {
			EditorInstantiation.InstantiateEmptySpineGameObject<SkeletonRenderer>("New SkeletonRenderer", true);
		}

		[MenuItem("GameObject/Spine/SkeletonAnimation", false, 10)]
		static public void CreateSkeletonAnimationGameObject () {
			EditorInstantiation.InstantiateEmptySpineGameObject<SkeletonAnimation>("New SkeletonAnimation", true);
		}

		[MenuItem("GameObject/Spine/Remove SkeletonGraphicPreview From This And Children", false, 20)]
		static public void RemoveSkeletonGraphicPreviewFromHierarchy () {
			if (Selection.gameObjects == null || Selection.gameObjects.Length == 0) return;
			var toProcess = new List<GameObject>();
			foreach (GameObject root in Selection.gameObjects) {
				toProcess.Add(root);
				CollectChildren(root.transform, toProcess);
			}
			var dirtyScenes = new HashSet<UnityEngine.SceneManagement.Scene>();
			int removed = 0;
			foreach (GameObject go in toProcess) {
				var preview = go.GetComponent<SkeletonGraphicEditorPreview>();
				if (preview == null) continue;
				var sg = go.GetComponent<SkeletonGraphic>();
				if (sg != null)
					sg.SetPreviewRemovedByUser();
				Undo.DestroyObjectImmediate(preview);
				removed++;
				if (go.scene.IsValid())
					dirtyScenes.Add(go.scene);
			}
			foreach (var scene in dirtyScenes)
				EditorSceneManager.MarkSceneDirty(scene);
			if (removed > 0) {
				EditorSceneManager.SaveOpenScenes();
				Debug.Log($"Removed SkeletonGraphicPreview from {removed} GameObject(s) and saved.");
			}
		}

		[MenuItem("GameObject/Spine/Remove SkeletonGraphicPreview From This And Children", true)]
		static public bool ValidateRemoveSkeletonGraphicPreviewFromHierarchy () {
			if (Selection.gameObjects == null || Selection.gameObjects.Length == 0) return false;
			foreach (GameObject root in Selection.gameObjects) {
				if (RootOrAnyChildHasPreview(root)) return true;
			}
			return false;
		}

		static void CollectChildren (Transform t, List<GameObject> outList) {
			for (int i = 0; i < t.childCount; i++) {
				Transform c = t.GetChild(i);
				outList.Add(c.gameObject);
				CollectChildren(c, outList);
			}
		}

		static bool RootOrAnyChildHasPreview (GameObject root) {
			if (root.GetComponent<SkeletonGraphicEditorPreview>() != null) return true;
			for (int i = 0; i < root.transform.childCount; i++) {
				if (RootOrAnyChildHasPreview(root.transform.GetChild(i).gameObject)) return true;
			}
			return false;
		}
	}
}
