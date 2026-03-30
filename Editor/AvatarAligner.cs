using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace UniMuse.Lab
{
    public class AvatarAligner : EditorWindow
    {
        // 設定用変数
        private float interval = 1.0f;
        private int maxPerRow = 0; // 0は無限
        private float zInterval = 1.5f;
        private bool usePushOut = true;

        private enum Direction { Right, Left }
        private Direction alignDirection = Direction.Right;

        private enum AlignMode { SimpleGrid, ZGroupSort }
        private AlignMode currentMode = AlignMode.SimpleGrid;

        private Vector2 scrollPos;
        private List<ZGroup> zGroups = new List<ZGroup>();

        // Z軸グループ管理用クラス
        private class ZGroup
        {
            public float zPosition;
            public bool isSelected = true;
            public List<GameObject> members = new List<GameObject>();
        }

        [MenuItem("UniMuse.lab/Avatar Aligner")]
        public static void ShowWindow()
        {
            GetWindow<AvatarAligner>("Avatar Aligner");
        }

        private void OnGUI()
        {
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            GUILayout.Label("Avatar Aligner - アバター整列ツール", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 基本設定セクション
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("基本設定", EditorStyles.boldLabel);
            interval = EditorGUILayout.FloatField("個体間の間隔 (X)", interval);
            alignDirection = (Direction)EditorGUILayout.EnumPopup("整列する方向", alignDirection);
            usePushOut = EditorGUILayout.Toggle("後ろのアバターを押し出す", usePushOut);
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // モード選択セクション
            EditorGUILayout.BeginVertical("box");
            GUILayout.Label("整列モード設定", EditorStyles.boldLabel);
            currentMode = (AlignMode)EditorGUILayout.EnumPopup("モード選択", currentMode);

            if (currentMode == AlignMode.SimpleGrid)
            {
                maxPerRow = EditorGUILayout.IntField("1列の最大数 (0で無効)", maxPerRow);
                if (maxPerRow > 0)
                {
                    zInterval = EditorGUILayout.FloatField("列の間隔 (Z)", zInterval);
                }
            }
            else
            {
                if (GUILayout.Button("シーン内のZ軸グループをスキャン"))
                {
                    ScanZGroups();
                }

                if (zGroups.Count > 0)
                {
                    EditorGUILayout.Space();
                    GUILayout.Label("整列対象にする列を選択:", EditorStyles.miniLabel);
                    foreach (var group in zGroups)
                    {
                        group.isSelected = EditorGUILayout.Toggle($"列 (Z座標: {group.zPosition:F2}) - {group.members.Count}体", group.isSelected);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("スキャンボタンを押してZ軸の列を特定してください。", MessageType.Info);
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space();

            // 実行ボタン
            GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button("整列を実行する", GUILayout.Height(40)))
            {
                ExecuteAlign();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndScrollView();
        }

        private void ScanZGroups()
        {
            zGroups.Clear();
            var allAvatars = FindAllAvatars();

            foreach (var avatar in allAvatars)
            {
                float z = avatar.transform.position.z;
                var group = zGroups.Find(g => Mathf.Abs(g.zPosition - z) < 0.5f);
                if (group == null)
                {
                    group = new ZGroup { zPosition = z };
                    zGroups.Add(group);
                }
                group.members.Add(avatar);
            }
            zGroups = zGroups.OrderBy(g => g.zPosition).ToList();
        }

        private void ExecuteAlign()
        {
            List<GameObject> targets = new List<GameObject>();
            bool isSelectionMode = Selection.gameObjects.Length > 0;

            if (isSelectionMode)
            {
                // 選択中アバターのみを対象にする
                foreach (var obj in Selection.gameObjects)
                {
                    if (IsAvatar(obj)) targets.Add(obj);
                }
            }
            else
            {
                // モードに応じて全体を対象にする
                if (currentMode == AlignMode.SimpleGrid)
                {
                    targets = FindAllAvatars();
                }
                else
                {
                    foreach (var group in zGroups)
                    {
                        if (group.isSelected) targets.AddRange(group.members);
                    }
                }
            }

            if (targets.Count == 0)
            {
                Debug.LogWarning("[Avatar Aligner] 対象のアバターが見つかりませんでした。");
                return;
            }

            // 選択解除（競合回避のため）
            var savedSelection = Selection.objects;
            Selection.objects = new Object[0];

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Avatar Align");
            int undoGroup = Undo.GetCurrentGroup();

            // 整列ロジックの開始
            // 基準点：ターゲットの中で最も進行方向とは逆端にあるアバターの座標
            targets = targets.OrderBy(a => a.transform.position.x).ToList();
            if (alignDirection == Direction.Left) targets.Reverse();

            float startX = targets[0].transform.position.x;
            float startZ = targets[0].transform.position.z;

            for (int i = 0; i < targets.Count; i++)
            {
                Undo.RecordObject(targets[i].transform, "Align Avatar");

                float xPos, zPos;
                if (currentMode == AlignMode.SimpleGrid && maxPerRow > 0)
                {
                    int row = i / maxPerRow;
                    int col = i % maxPerRow;
                    xPos = startX + (col * interval * (alignDirection == Direction.Right ? 1 : -1));
                    zPos = startZ + (row * zInterval);
                }
                else
                {
                    xPos = startX + (i * interval * (alignDirection == Direction.Right ? 1 : -1));
                    zPos = targets[i].transform.position.z; // ZGroupモード時は元のZを維持
                }

                targets[i].transform.position = new Vector3(xPos, targets[i].transform.position.y, zPos);
            }

            // 押し出しロジック
            if (usePushOut)
            {
                PushOutNonSelected(targets, alignDirection);
            }

            Undo.CollapseUndoOperations(undoGroup);
            Selection.objects = savedSelection; // 選択状態を戻す
            Debug.Log($"[Avatar Aligner] {targets.Count}体のアバターを整列しました。");
        }

        private void PushOutNonSelected(List<GameObject> sortedTargets, Direction dir)
        {
            float lastX = sortedTargets[sortedTargets.Count - 1].transform.position.x;
            var allAvatars = FindAllAvatars();
            
            foreach (var other in allAvatars)
            {
                if (sortedTargets.Contains(other)) continue;

                bool isAhead = (dir == Direction.Right) ? 
                    (other.transform.position.x > lastX - interval * 0.1f) : 
                    (other.transform.position.x < lastX + interval * 0.1f);

                // Z軸も近い場合のみ押し出す（同じ列とみなす）
                bool isSameZ = false;
                foreach(var t in sortedTargets) {
                    if (Mathf.Abs(t.transform.position.z - other.transform.position.z) < 0.5f) {
                        isSameZ = true;
                        break;
                    }
                }

                if (isAhead && isSameZ)
                {
                    Undo.RecordObject(other.transform, "Push Out Avatar");
                    float pushDist = interval; // 最低でも1間隔分は離す
                    float newX = lastX + (pushDist * (dir == Direction.Right ? 1 : -1));
                    
                    // すでに十分に離れている場合は動かさない
                    bool needsPush = (dir == Direction.Right) ? (other.transform.position.x < newX) : (other.transform.position.x > newX);
                    if(needsPush)
                    {
                        other.transform.position = new Vector3(newX, other.transform.position.y, other.transform.position.z);
                        // 再帰的に後ろのアバターもチェックするために再度lastXを更新してループ
                        lastX = newX;
                    }
                }
            }
        }

        private List<GameObject> FindAllAvatars()
        {
            var result = new List<GameObject>();
            var allObjects = GameObject.FindObjectsOfType<GameObject>();
            foreach (var obj in allObjects)
            {
                if (IsAvatar(obj)) result.Add(obj);
            }
            return result;
        }

        private bool IsAvatar(GameObject obj)
        {
            if (obj == null) return false;
#if VRC_SDK_VRCSDK3
            return obj.GetComponent<VRCAvatarDescriptor>() != null;
#else
            // SDKがない場合は名前やAnimatorなどの汎用的な判定をフォールバックとして置くことも可能
            // 今回は既存ロジック維持のためVRC判定を優先
            return obj.name.Contains("Avatar") || obj.GetComponent<Animator>() != null;
#endif
        }
    }
}