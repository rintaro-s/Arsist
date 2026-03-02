// ==============================================
// Arsist Engine - VRM Metadata Display
// Assets/Arsist/Runtime/VRM/VRMMetadataDisplay.cs
// ==============================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arsist.Runtime.VRM
{
    /// <summary>
    /// VRM ロード後、その能力情報（表情・ボーン・Transform）を Inspector で表示するコンポーネント。
    /// Editor のみで動作（ゲーム実行中は検査機能のみ）
    /// </summary>
    [ExecuteAlways]
    public class VRMMetadataDisplay : MonoBehaviour
    {
        [SerializeField] public string vrmAssetId = "(unset)";
        
        [SerializeField] public VRMMetadata metadata = new VRMMetadata();

        /// <summary>
        /// VRM メタデータを更新する
        /// </summary>
        public void UpdateMetadata(string assetId, Animator animator)
        {
            vrmAssetId = assetId;

            if (metadata == null)
                metadata = new VRMMetadata();

            metadata.expressionCount = 0;
            metadata.expressions = new List<string>();
            metadata.hasHumanoid = false;
            metadata.humanoidBoneCount = 0;
            metadata.humanoidBones = new List<string>();

            // --- BlendShape / 表情を検出 ---
            var skinnedMeshes = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var seenExprNames = new HashSet<string>();

            foreach (var smr in skinnedMeshes)
            {
                if (smr == null || smr.sharedMesh == null) continue;

                int blendCount = smr.sharedMesh.blendShapeCount;
                for (int i = 0; i < blendCount; i++)
                {
                    var name = smr.sharedMesh.GetBlendShapeName(i);
                    if (!string.IsNullOrEmpty(name) && seenExprNames.Add(name))
                    {
                        metadata.expressions.Add(name);
                    }
                }
            }
            metadata.expressionCount = metadata.expressions.Count;

            // --- Humanoid ボーン検出 ---
            if (animator != null && animator.isHuman)
            {
                metadata.hasHumanoid = true;

                foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;

                    Transform boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        metadata.humanoidBones.Add(bone.ToString());
                    }
                }

                metadata.humanoidBoneCount = metadata.humanoidBones.Count;
            }

            // --- Transform 情報 ---
            var t = transform;
            metadata.position = t.position;
            metadata.rotation = t.eulerAngles;
            metadata.scale = t.localScale;

            Debug.Log($"[VRMMetadataDisplay] Updated '{assetId}': expressions={metadata.expressionCount}, " +
                      $"humanoid={metadata.hasHumanoid}, bones={metadata.humanoidBoneCount}");
        }

        private void OnValidate()
        {
            // Editor で Prefab 等が変更されたとき
            if (!Application.isPlaying && metadata != null)
            {
                var animator = GetComponent<Animator>();
                if (animator != null)
                {
                    // ボーン数が 0 だが Animator がある場合、再検出を試みる
                    if (metadata.humanoidBoneCount == 0 && animator.isHuman)
                    {
                        UpdateMetadata(vrmAssetId, animator);
                    }
                }
            }
        }

        // ============================================
        // メタデータクラス
        // ============================================

        [Serializable]
        public class VRMMetadata
        {
            [SerializeField] public int expressionCount = 0;
            [SerializeField] public List<string> expressions = new List<string>();

            [SerializeField] public bool hasHumanoid = false;
            [SerializeField] public int humanoidBoneCount = 0;
            [SerializeField] public List<string> humanoidBones = new List<string>();

            [SerializeField] public Vector3 position = Vector3.zero;
            [SerializeField] public Vector3 rotation = Vector3.zero;
            [SerializeField] public Vector3 scale = Vector3.one;
        }
    }
}
