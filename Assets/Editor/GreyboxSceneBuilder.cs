using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpatialRhythm.EditorTools
{
    /// <summary>
    /// 生成灰盒场景。场景里只放一个挂 <see cref="GreyboxBootstrap"/> 的空物体，
    /// 其余系统全部在运行时构建——灰盒阶段结构改动频繁，代码装配比场景引用更好维护。
    /// </summary>
    public static class GreyboxSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Greybox.unity";

        [MenuItem("SpatialRhythm/重建灰盒场景")]
        public static void BuildScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("Greybox");
            root.AddComponent<GreyboxBootstrap>();

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            if (!saved)
            {
                Debug.LogError($"[GreyboxSceneBuilder] 保存场景失败：{ScenePath}");
                return;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RegisterInBuildSettings();

            Debug.Log($"[GreyboxSceneBuilder] 已生成 {ScenePath}");
        }

        private static void RegisterInBuildSettings()
        {
            EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].path == ScenePath)
                {
                    return;
                }
            }

            var updated = new EditorBuildSettingsScene[existing.Length + 1];
            System.Array.Copy(existing, updated, existing.Length);
            updated[existing.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = updated;
        }
    }
}
