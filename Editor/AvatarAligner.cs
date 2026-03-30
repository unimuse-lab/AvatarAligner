using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace UniMuse.Lab
{
    public class AvatarAligner : EditorWindow
    {
        private float intervalX = 1.0f;
        private float intervalZ = 1.5f;
        private int maxCountPerRow = 0; 
        private bool autoRotationReset = true;
        private bool isPushSelectedToBack = true; // 表記を「選択物を最後尾に送る」に変更

        private enum DirectionX { Right = -1, Left = 1 }
        private DirectionX alignDirX = DirectionX.Right;

        private enum DirectionZ { Backward = -1, Forward = 1 }
        private DirectionZ alignDirZ = DirectionZ.Backward;

        private enum AlignMode { ResetAll, KeepGroups }
        private AlignMode currentMode = AlignMode.ResetAll;

        private Vector2 scrollPos;
        private List<ZGroup> zGroups = new List<ZGroup>();

        private class ZGroup
        {
            public float zPosition;
            public bool isSelected = true;
            public List<GameObject> members = new List<GameObject>();
        }

        [MenuItem("UniMuse.lab/Avatar Aligner Ultimate V15")]
        public static void ShowWindow() => GetWindow<AvatarAligner>("Avatar Aligner");

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label("Avatar Aligner - 究極統合版 V15", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("【 1. 配置・間隔設定 】", EditorStyles.boldLabel);
            intervalX = EditorGUILayout.FloatField("横の間隔 (X)", intervalX);
            intervalZ = EditorGUILayout.FloatField("列の間隔 (Z)", intervalZ);
            maxCountPerRow = Mathf.Max(0, EditorGUILayout.IntField("1列の最大数 (0で無制限)", maxCountPerRow));

            alignDirX = (DirectionX)EditorGUILayout.Popup("横の並び順", (int)(alignDirX == DirectionX.Right ? 0 : 1), new string[] { "右へ並べる (-X)", "左へ並べる (+X)" }) == 0 ? DirectionX.Right : DirectionX.Left;
            alignDirZ = (DirectionZ)EditorGUILayout.Popup("列の伸びる方向", (int)(alignDirZ == DirectionZ.Backward ? 0 : 1), new string[] { "奥へ作る (-Z)", "手前へ作る (+Z)" }) == 0 ? DirectionZ.Backward : DirectionZ.Forward;
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("【 2. 実行ルールとオプション 】", EditorStyles.boldLabel);
            currentMode = (AlignMode)EditorGUILayout.Popup("整列モード", (int)currentMode, new string[] {
                "完全にリセット (0,0,0基準で詰めなおす)",
                "現在の列を維持 (Z軸グループ内のみ)"
            });

            EditorGUILayout.Space();
            isPushSelectedToBack = EditorGUILayout.Toggle("選択中をグループ最後尾へ", isPushSelectedToBack);
            autoRotationReset = EditorGUILayout.Toggle("回転を0にリセット", autoRotationReset);

            if (currentMode == AlignMode.KeepGroups)
            {
                EditorGUILayout.Space();
                if (GUILayout.Button("現在の配置をスキャン")) ScanZGroups();
                foreach (var group in zGroups)
                {
                    group.isSelected = EditorGUILayout.Toggle($"奥行き {group.zPosition:F2}m の列 ({group.members.Count}体)", group.isSelected);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            var selected = GetSelectedAvatars();
            string btnText = selected.Count > 0 ? $"{selected.Count} 体を整列実行" : "シーン内の全アバターを整列実行";

            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(btnText, GUILayout.Height(50)))
            {
                ExecuteMainAlign();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndScrollView();
        }

        private void ExecuteMainAlign()
        {
            var selected = GetSelectedAvatars();
            List<GameObject> targets = (selected.Count > 0) ? selected : FindAllAvatarsInScene();

            if (targets.Count == 0) return;

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Avatar Align V15");
            int groupIndex = Undo.GetCurrentGroup();

            var parents = targets.Select(t => t.transform.parent).Distinct().ToList();
            foreach (var p in parents)
            {
                SortAndAlignWithinParent(p, targets, selected);
            }

            Undo.CollapseUndoOperations(groupIndex);
            Debug.Log($"[Avatar Aligner] 整列完了。選択物の後方移動を適用しました。");
        }

        private void SortAndAlignWithinParent(Transform parent, List<GameObject> globalTargets, List<GameObject> selectedTargets)
        {
            List<Transform> avatars = new List<Transform>();
            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (IsAvatar(child.gameObject)) avatars.Add(child);
                }
            }
            else
            {
                foreach (var rootGO in SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (IsAvatar(rootGO)) avatars.Add(rootGO.transform);
                }
            }

            if (avatars.Count == 0) return;

            int effectiveMax = (maxCountPerRow <= 0) ? int.MaxValue : maxCountPerRow;

            if (currentMode == AlignMode.ResetAll)
            {
                // 全体をひとつの順序として処理
                ProcessGroupMembers(avatars.OrderBy(t => t.GetSiblingIndex()).ToList(), selectedTargets, effectiveMax, true, 0);
            }
            else
            {
                // 各列ごとに処理
                var groups = avatars.GroupBy(t => Mathf.Round(t.localPosition.z * 10f) / 10f)
                                    .OrderBy(g => g.Key * (float)alignDirZ)
                                    .ToList();

                foreach (var g in groups)
                {
                    ProcessGroupMembers(g.OrderBy(t => t.GetSiblingIndex()).ToList(), selectedTargets, effectiveMax, false, g.Key);
                }
            }
        }

        private void ProcessGroupMembers(List<Transform> originalMembers, List<GameObject> selectedTargets, int effectiveMax, bool isResetMode, float originalZ)
        {
            List<Transform> finalOrder;

            // --- 順序の確定ロジック ---
            if (isPushSelectedToBack && selectedTargets.Count > 0)
            {
                // このグループ内で「選択されていないもの」を抽出
                var nonSelected = originalMembers.Where(m => !selectedTargets.Contains(m.gameObject)).ToList();
                // このグループ内で「選択されているもの」を抽出
                var selectedInGroup = originalMembers.Where(m => selectedTargets.Contains(m.gameObject)).ToList();
                
                // 【非選択】を前に、【選択済み】を後に結合（これで 2-4 が後ろに回る）
                finalOrder = nonSelected.Concat(selectedInGroup).ToList();

                // ヒエラルキーのインデックスを、このグループの元の範囲内で書き換える
                int startSibling = originalMembers.Min(t => t.GetSiblingIndex());
                for (int i = 0; i < finalOrder.Count; i++)
                {
                    Undo.RecordObject(finalOrder[i], "Reorder");
                    finalOrder[i].SetSiblingIndex(startSibling + i);
                }
            }
            else
            {
                finalOrder = new List<Transform>(originalMembers);
            }

            // --- 座標の割り当てロジック ---
            for (int i = 0; i < finalOrder.Count; i++)
            {
                Undo.RecordObject(finalOrder[i], "Align Position");
                
                int rowOffset = i / effectiveMax;
                int col = i % effectiveMax;
                
                float x = col * intervalX * (float)alignDirX;
                float z = isResetMode ? 
                    (i / effectiveMax * intervalZ * (float)alignDirZ) : 
                    originalZ + (rowOffset * intervalZ * (float)alignDirZ);

                finalOrder[i].localPosition = new Vector3(x, finalOrder[i].localPosition.y, z);
                if (autoRotationReset) finalOrder[i].localRotation = Quaternion.identity;
            }
        }

        private void ScanZGroups()
        {
            zGroups.Clear();
            var all = FindAllAvatarsInScene();
            foreach (var a in all)
            {
                float z = Mathf.Round(a.transform.localPosition.z * 10f) / 10f;
                var g = zGroups.Find(x => Mathf.Abs(x.zPosition - z) < 0.1f);
                if (g == null) { g = new ZGroup { zPosition = z }; zGroups.Add(g); }
                g.members.Add(a);
            }
            zGroups = zGroups.OrderBy(g => g.zPosition).ToList();
        }

        private List<GameObject> GetSelectedAvatars()
        {
            return Selection.gameObjects
                .Where(go => IsAvatar(go))
                .OrderBy(go => go.transform.GetSiblingIndex())
                .ToList();
        }

        private List<GameObject> FindAllAvatarsInScene()
        {
            List<GameObject> allObjects = new List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                GetAvatarsRecursive(root, allObjects);
            }
            return allObjects;
        }

        private void GetAvatarsRecursive(GameObject obj, List<GameObject> result)
        {
            if (IsAvatar(obj))
            {
                result.Add(obj);
                return;
            }
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                GetAvatarsRecursive(obj.transform.GetChild(i).gameObject, result);
            }
        }

        private bool IsAvatar(GameObject go)
        {
            if (go == null) return false;
            return go.GetComponent("VRC_AvatarDescriptor") != null ||
                   go.GetComponent("VRCAvatarDescriptor") != null ||
                   go.name.ToLower().Contains("vrcavatar");
        }
    }
}