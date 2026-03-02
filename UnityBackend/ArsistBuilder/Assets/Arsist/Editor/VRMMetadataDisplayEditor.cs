// ==============================================
// Arsist Engine - VRM Metadata Display Inspector
// Assets/Arsist/Editor/VRMMetadataDisplayEditor.cs
// ==============================================
#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using Arsist.Runtime.VRM;

namespace Arsist.Editor
{
    /// <summary>
    /// VRMMetadataDisplay のカスタムインスペクタ。
    /// 表情一覧とボーン一覧を折りたたみ可能な形で表示する。
    /// </summary>
    [CustomEditor(typeof(VRMMetadataDisplay))]
    public class VRMMetadataDisplayEditor : UnityEditor.Editor
    {
        private bool _showExpressions = true;
        private bool _showBones = true;
        private Vector2 _expressionScroll = Vector2.zero;
        private Vector2 _boneScroll = Vector2.zero;

        public override void OnInspectorGUI()
        {
            var vrmDisplay = target as VRMMetadataDisplay;
            if (vrmDisplay == null) return;

            EditorGUILayout.LabelField("VRM メタデータ情報", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Asset ID
            EditorGUILayout.LabelField("Asset ID", vrmDisplay.vrmAssetId, EditorStyles.wordWrappedLabel);

            EditorGUILayout.Space();

            var metadata = vrmDisplay.metadata;
            if (metadata == null)
            {
                EditorGUILayout.HelpBox("メタデータなし", MessageType.Info);
                return;
            }

            // --- Transform 情報 ---
            EditorGUILayout.LabelField("Transform", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            EditorGUILayout.Vector3Field("Position", metadata.position);
            EditorGUILayout.Vector3Field("Rotation", metadata.rotation);
            EditorGUILayout.Vector3Field("Scale", metadata.scale);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space();

            // --- 表情（BlendShape）情報 ---
            _showExpressions = EditorGUILayout.Foldout(_showExpressions, 
                $"表情 ({metadata.expressionCount})", 
                EditorStyles.foldout);

            if (_showExpressions)
            {
                EditorGUI.indentLevel++;

                if (metadata.expressionCount == 0)
                {
                    EditorGUILayout.HelpBox("BlendShape 表情がありません", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"利用可能な表情: {metadata.expressionCount} 種\n" +
                        $"Control.py で使用可能。例: ctrl.set_expression(id, \"{metadata.expressions[0]}\", 80)",
                        MessageType.None
                    );

                    // スクロール可能なリスト
                    _expressionScroll = EditorGUILayout.BeginScrollView(_expressionScroll, 
                        GUILayout.Height(Mathf.Min(metadata.expressionCount * 18 + 10, 300)));

                    for (int i = 0; i < metadata.expressions.Count; i++)
                    {
                        var expr = metadata.expressions[i];
                        EditorGUILayout.LabelField($"  [{i + 1:D2}]  {expr}", EditorStyles.label);
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // --- Humanoid ボーン情報 ---
            _showBones = EditorGUILayout.Foldout(_showBones,
                $"Humanoid ボーン ({(metadata.hasHumanoid ? metadata.humanoidBoneCount : 0)})",
                EditorStyles.foldout);

            if (_showBones)
            {
                EditorGUI.indentLevel++;

                if (!metadata.hasHumanoid)
                {
                    EditorGUILayout.HelpBox("Humanoid Animator が見つかりません", MessageType.Warning);
                }
                else if (metadata.humanoidBoneCount == 0)
                {
                    EditorGUILayout.HelpBox("Humanoid ボーンが設定されていません", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"利用可能なボーン: {metadata.humanoidBoneCount} 種\n" +
                        $"Control.py で使用可能。例: ctrl.set_bone_rotation(id, \"{metadata.humanoidBones[0]}\", 0, 0, 0)",
                        MessageType.None
                    );

                    // スクロール可能なリスト
                    _boneScroll = EditorGUILayout.BeginScrollView(_boneScroll,
                        GUILayout.Height(Mathf.Min(metadata.humanoidBoneCount * 18 + 10, 300)));

                    for (int i = 0; i < metadata.humanoidBones.Count; i++)
                    {
                        var bone = metadata.humanoidBones[i];
                        EditorGUILayout.LabelField($"  [{i + 1:D2}]  {bone}", EditorStyles.label);
                    }

                    EditorGUILayout.EndScrollView();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            // --- 更新ボタン ---
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);
            if (GUILayout.Button("メタデータを再更新", GUILayout.Height(30)))
            {
                var animator = vrmDisplay.GetComponent<Animator>();
                vrmDisplay.UpdateMetadata(vrmDisplay.vrmAssetId, animator);
                EditorUtility.SetDirty(vrmDisplay);
            }
        }
    }
}

#endif
