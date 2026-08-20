using UnityEngine;

namespace SpatialRhythm.Core
{
    /// <summary>
    /// 灰盒的材质工厂。
    ///
    /// 集中在这里是因为踩过一个坑：Shader.Find 找内置着色器在 Editor 里能成功，
    /// 但打包后返回 null（未被引用的内置着色器不进包），播放器直接在 Awake 崩掉。
    /// 现在统一走 Resources 下自带的着色器。
    /// </summary>
    public static class StageMaterials
    {
        private const string ShaderResourcePath = "Shaders/UnlitNote";
        private const string LineShaderResourcePath = "Shaders/UnlitLine";

        private static Shader _shader;
        private static Shader _lineShader;

        public static Shader UnlitShader
        {
            get
            {
                if (_shader != null)
                {
                    return _shader;
                }

                _shader = Resources.Load<Shader>(ShaderResourcePath);

                if (_shader == null)
                {
                    // 兜底：Editor 里总能找到，真机上走不到这条分支。
                    _shader = Shader.Find("Unlit/Color");
                }

                if (_shader == null)
                {
                    Debug.LogError($"[StageMaterials] 找不到着色器 Resources/{ShaderResourcePath}");
                }

                return _shader;
            }
        }

        public static Material Create(Color color)
        {
            var material = new Material(UnlitShader) { color = color };
            material.enableInstancing = true;
            return material;
        }

        /// <summary>支持顶点色的透明线材质，供 LineRenderer 的 colorGradient 使用。</summary>
        public static Material CreateLine(Color color)
        {
            if (_lineShader == null)
            {
                _lineShader = Resources.Load<Shader>(LineShaderResourcePath);
            }

            if (_lineShader == null)
            {
                Debug.LogError($"[StageMaterials] 找不到着色器 Resources/{LineShaderResourcePath}");
                return Create(color);
            }

            return new Material(_lineShader) { color = color };
        }
    }
}
