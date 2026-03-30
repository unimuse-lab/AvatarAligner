#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System.IO;

// VRChatのアバターコンポーネントを認識するために必要
#if VRC_SDK_VRCSDK3
using VRC.SDK3.Avatars.Components;
#endif

namespace UniMuseLab.AvatarAligner
{
    /// <summary>
    /// AvatarAlignerの設定値を保持するScriptableObject
    /// </summary>
    public class AvatarAlignerData : ScriptableObject
    {
        public float spacingInterval = 1.0f;
        public AvatarAligner.Direction modeDirection = AvatarAligner.Direction.Positive;
        public AvatarAligner.SortTarget modeSort = AvatarAligner.SortTarget.HierarchyOrder;
        public AvatarAligner.AlignmentBase modeBase = AvatarAligner.AlignmentBase.FirstObject;
        public float customStartX = 0.0f;
    }

    /// <summary>
    /// VRChatアバターを等間隔に整列させるエディタウィンドウ
    /// </summary>
    public class AvatarAligner : EditorWindow
    {
        public AvatarAlignerData data;

        // 保存先パスの定義（BookmarkerWindow.cs の仕様に準拠）
        private const string SAVE_ROOT = "Assets/UniMuseData";
        private const string SAVE_FOLDER = "AvatarAligner";
        private const string DATA_FILE_NAME = "AvatarAlignerData.asset";

        public enum Direction
        {
            Positive, // 正方向 (+X) ※ユーザー指定の「左側（正）」に相当
            Negative  // 負方向 (-X) ※ユーザー指定の「右側（負）」に相当
        }

        public enum SortTarget
        {
            HierarchyOrder, // ヒエラルキーの並び順を優先して並べる
            PositionOrder   // 現在の位置（一番端からの距離）を優先して並べる
        }

        public enum AlignmentBase
        {
            FirstObject, // 1体目のアバターの現在位置を基準にする
            WorldZero,   // ワールドの原点 (X = 0.0) を基準にする
            CustomX      // 指定した任意のX座標を基準にする
        }

        [MenuItem("UniMuse.lab/Avatar Aligner")]
        public static void ShowWindow()
        {
            var window = GetWindow<AvatarAligner>();
            window.titleContent = new GUIContent("Avatar Aligner");
            window.Show();
        }

        private void OnEnable()
        {
            data = LoadOrCreateData();
        }

        /// <summary>
        /// 指定されたパスにデータを読み込むか、存在しない場合は作成する
        /// </summary>
        public AvatarAlignerData LoadOrCreateData()
        {
            string folderPath = Path.Combine(SAVE_ROOT, SAVE_FOLDER).Replace("\\", "/");
            string filePath = Path.Combine(folderPath, DATA_FILE_NAME).Replace("\\", "/");

            var loadedData = AssetDatabase.LoadAssetAtPath<AvatarAlignerData>(filePath);

            if (loadedData == null)
            {
                // UniMuseData ルートフォルダの作成
                if (!AssetDatabase.IsValidFolder(SAVE_ROOT))
                {
                    AssetDatabase.CreateFolder("Assets", "UniMuseData");
                }
                // AvatarAligner 個別フォルダの作成
                if (!AssetDatabase.IsValidFolder(folderPath))
                {
                    AssetDatabase.CreateFolder(SAVE_ROOT, SAVE_FOLDER);
                }

                loadedData = ScriptableObject.CreateInstance<AvatarAlignerData>();
                AssetDatabase.CreateAsset(loadedData, filePath);
                AssetDatabase.SaveAssets();
            }
            return loadedData;
        }

        private void OnGUI()
        {
            if (data == null) { data = LoadOrCreateData(); }

            GUILayout.Label("VRChatアバター等間隔整列ツール", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginChangeCheck();

            data.spacingInterval = EditorGUILayout.FloatField("間隔 (Interval)", data.spacingInterval);
            data.modeDirection = (Direction)EditorGUILayout.EnumPopup("並べる方向", data.modeDirection);
            data.modeSort = (SortTarget)EditorGUILayout.EnumPopup("並び順の基準", data.modeSort);
            data.modeBase = (AlignmentBase)EditorGUILayout.EnumPopup("開始位置の基準", data.modeBase);

            if (data.modeBase == AlignmentBase.CustomX)
            {
                EditorGUI.indentLevel++;
                data.customStartX = EditorGUILayout.FloatField("開始X座標", data.customStartX);
                EditorGUI.indentLevel--;
            }

            // 設定が変更されたら保存する
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssets();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("整列を実行"))
            {
                ExecuteAlignment();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "【仕様】\n" +
                "・選択中、またはヒエラルキー上の「VRC_AvatarDescriptor」を持つオブジェクトのみを整列対象にします。\n\n" +
                "【並べる方向について】\n" +
                "・Positive: 正方向 (+X) に並べます。※「左側（正）」に相当します。\n" +
                "・Negative: 負方向 (-X) に並べます。※「右側（負）」に相当します。\n\n" +
                "【並び順について】\n" +
                "・HierarchyOrder: ヒエラルキーの上のアバターから順に並べます。\n" +
                "・PositionOrder: X座標が端にあるアバターから順に並べます。同値ならヒエラルキー順です。\n\n" +
                "【開始位置について】\n" +
                "・FirstObject: リストの1体目の現在位置を起点にします。\n" +
                "・WorldZero: X座標の 0.0 を起点にします。\n" +
                "・CustomX: 指定した任意のX座標を起点にします。",
                MessageType.Info);
        }

        private void ExecuteAlignment()
        {
            // 1. 対象オブジェクトの取得とフィルタリング
            List<GameObject> allPotentialTargets = new List<GameObject>();
            if (Selection.gameObjects.Length > 0)
            {
                allPotentialTargets = Selection.gameObjects.ToList();
            }
            else
            {
                Scene activeScene = SceneManager.GetActiveScene();
                allPotentialTargets = activeScene.GetRootGameObjects().ToList();
            }

            List<GameObject> targets = allPotentialTargets.Where(g => IsAvatar(g)).ToList();

            if (targets.Count < 2)
            {
                Debug.LogWarning("[Avatar Aligner] 整列対象のアバターが2つ以上見つかりませんでした。");
                return;
            }

            // 2. ヒエラルキーの完全な順番を取得
            List<GameObject> allHierarchyObjects = new List<GameObject>();
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                AddChildrenRecursive(root, allHierarchyObjects);
            }

            // 3. 指定されたモードに従ってソート
            if (data.modeSort == SortTarget.HierarchyOrder)
            {
                targets = targets.OrderBy(g => allHierarchyObjects.IndexOf(g)).ToList();
            }
            else if (data.modeSort == SortTarget.PositionOrder)
            {
                if (data.modeDirection == Direction.Positive)
                {
                    targets = targets.OrderBy(g => g.transform.position.x)
                                     .ThenBy(g => allHierarchyObjects.IndexOf(g))
                                     .ToList();
                }
                else
                {
                    targets = targets.OrderByDescending(g => g.transform.position.x)
                                     .ThenBy(g => allHierarchyObjects.IndexOf(g))
                                     .ToList();
                }
            }

            // 4. アンドゥ処理の登録
            Undo.RecordObjects(targets.Select(g => g.transform).ToArray(), "Align Avatars X");

            // 5. 開始位置（X座標）の決定
            float startX = 0f;
            switch (data.modeBase)
            {
                case AlignmentBase.FirstObject:
                    startX = targets[0].transform.position.x;
                    break;
                case AlignmentBase.WorldZero:
                    startX = 0.0f;
                    break;
                case AlignmentBase.CustomX:
                    startX = data.customStartX;
                    break;
            }

            // 6. 整列処理
            float directionMultiplier = (data.modeDirection == Direction.Positive) ? 1.0f : -1.0f;

            for (int i = 0; i < targets.Count; i++)
            {
                Vector3 newPos = targets[i].transform.position;
                newPos.x = startX + (data.spacingInterval * i * directionMultiplier);
                targets[i].transform.position = newPos;
            }

            Debug.Log($"[Avatar Aligner] {targets.Count}体のアバターを整列しました。");
        }

        private bool IsAvatar(GameObject go)
        {
            return go.GetComponent("VRC_AvatarDescriptor") != null ||
                   go.GetComponent("VRCAvatarDescriptor") != null;
        }

        private void AddChildrenRecursive(GameObject obj, List<GameObject> list)
        {
            list.Add(obj);
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                AddChildrenRecursive(obj.transform.GetChild(i).gameObject, list);
            }
        }
    }
}
#endif