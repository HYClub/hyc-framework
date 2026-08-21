// ============================================================
// HYC Framework - 配置模块(Editor)
// 文件: Editor/DataEditor/ConfigModelPreviewWindow.cs
// 说明: 配置模型预览窗口 - 从配置的 Addressable 地址加载模型,
//       渲染模型 + 动画下拉播放 + 拖拽旋转
// ============================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HYC.Framework.Config.Editor
{
    public class ConfigModelPreviewWindow : EditorWindow
    {
        private string _address;
        private GameObject _model;
        private GameObject _previewInstance;
        private PreviewRenderUtility _preview;
        private Bounds _bounds;
        private Vector2 _cameraEuler = new Vector2(0f, 0f);
        private Vector2 _cameraPan = Vector2.zero;
        private float _cameraDist = 4f;
        private bool _animChanged = true;
        private List<string> _animNames = new List<string>();
        private List<string> _animUsages = new List<string>(); // 每个动画被哪个字段使用
        private int _animIndex;
        private bool _playing = true;
        private double _lastTime;

        // 动画字段映射(配置字段名 → 动画状态名), 由外部注入
        private Dictionary<string, string> _fieldToAnim = new Dictionary<string, string>();
        private Dictionary<string, string> _animToField = new Dictionary<string, string>();

        public static ConfigModelPreviewWindow OpenWindow(string address)
        {
            var w = GetWindow<ConfigModelPreviewWindow>();
            w.titleContent = new GUIContent($"模型预览: {address}");
            w.minSize = new Vector2(400, 450);
            w._address = address;
            w.LoadModel();
            w.Show();
            return w;
        }

        /// <summary>注入动画字段映射(字段名 → 动画名), 用于标注使用状态。</summary>
        public void SetAnimMapping(Dictionary<string, string> fieldToAnim)
        {
            _fieldToAnim = fieldToAnim ?? new Dictionary<string, string>();
            _animToField.Clear();
            foreach (var kv in _fieldToAnim)
                _animToField[kv.Value] = kv.Key;
        }

        private void OnEnable()
        {
            _lastTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            Cleanup();
        }

        /// <summary>每帧推进预览实例动画(Editor 时间驱动)。</summary>
        private void OnEditorUpdate()
        {
            if (!_playing || _previewInstance == null) return;
            var animator = _previewInstance.GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.Update(0.016f); // 模拟 60fps
            Repaint();
        }

        private void Cleanup()
        {
            if (_previewInstance != null) DestroyImmediate(_previewInstance);
            if (_preview != null) _preview.Cleanup();
            _previewInstance = null;
            _preview = null;
        }

        private void LoadModel()
        {
            Cleanup();
            _model = null;
            _animNames.Clear();
            _animUsages.Clear();
            _animIndex = 0;

            if (string.IsNullOrEmpty(_address))
            {
                Debug.LogWarning("[预览] 地址为空");
                return;
            }

            // Addressables 加载
            var handle = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(_address);
            var go = handle.WaitForCompletion();
            if (go == null)
            {
                Debug.LogWarning($"[预览] 加载失败: {_address}(未标 Addressable?)");
                return;
            }

            _model = go;
            if (_preview == null)
                _preview = new PreviewRenderUtility();

            _previewInstance = _preview.InstantiatePrefabInScene(_model);
            _previewInstance.transform.position = Vector3.zero;
            _previewInstance.transform.rotation = Quaternion.identity;

            // 收集动画状态
            CollectAnimations();
            Repaint();
        }

        private void CollectAnimations()
        {
            var animator = _previewInstance != null ? _previewInstance.GetComponentInChildren<Animator>() : null;
            if (animator == null) return;

            var controller = animator.runtimeAnimatorController;
            if (controller == null) return;

            var allStates = new HashSet<string>();
            foreach (var clip in controller.animationClips)
            {
                if (clip != null)
                    allStates.Add(clip.name);
            }

            foreach (var s in allStates)
            {
                _animNames.Add(s);
                _animUsages.Add(_animToField.TryGetValue(s, out var f) ? $"已用于: {f}" : "未使用");
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("模型地址: " + _address, EditorStyles.boldLabel);

            if (_model == null)
            {
                EditorGUILayout.HelpBox($"模型未加载(地址: {_address}).\n请确认该资源已标为 Addressable.", MessageType.Warning);
                if (GUILayout.Button("重新加载"))
                    LoadModel();
                return;
            }

            // 预览区
            var rect = GUILayoutUtility.GetRect(position.width - 10, position.height - 120);
            if (rect.width > 1 && rect.height > 1)
            {
                // 拖拽旋转
                HandleDrag(rect);

                if (Event.current.type == EventType.Repaint && _preview != null)
                {
                    _preview.BeginPreview(rect, GUIStyle.none);
                    var camera = _preview.camera;

                    // 播放动画(切换时从头播, 之后让 Animator 自己推进)
                    if (_playing && _previewInstance != null && _animNames.Count > 0 && _animIndex >= 0 && _animIndex < _animNames.Count)
                    {
                        var animator = _previewInstance.GetComponentInChildren<Animator>();
                        if (animator != null)
                        {
                            if (_animChanged)
                            {
                                animator.Play(_animNames[_animIndex], 0, 0f);
                                _animChanged = false;
                            }
                        }
                    }

                    var bounds = GetBounds();
                    var backDistance = _cameraDist;
                    var fov = 60f;
                    if (bounds.size.y > 0 && _cameraDist == 4f)
                        backDistance = (bounds.size.y * 0.5f) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) + 0.5f;

                    camera.transform.position = Vector3.zero;
                    camera.transform.rotation = Quaternion.Euler(-_cameraEuler.y, -_cameraEuler.x, 0);
                    camera.transform.position = camera.transform.forward * -backDistance;
                    // 平移(中键/右键拖)
                    camera.transform.position += camera.transform.right * _cameraPan.x + camera.transform.up * _cameraPan.y;

                    camera.clearFlags = CameraClearFlags.Color;
                    camera.backgroundColor = new Color(0.19f, 0.30f, 0.47f, 1f);
                    camera.fieldOfView = fov;
                    camera.farClipPlane = 60000f;
                    camera.nearClipPlane = 0.01f;

                    _preview.lights[0].intensity = 0.8f;
                    _preview.lights[1].intensity = 0.8f;
                    _preview.ambientColor = new Color(0.3f, 0.3f, 0.3f, 1f);

                    camera.Render();
                    _preview.EndAndDrawPreview(rect);

                    // 强制每帧刷新动画
                    if (_playing)
                        Repaint();
                }
            }

            // 动画下拉
            if (_animNames.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                var options = new string[_animNames.Count];
                for (int i = 0; i < _animNames.Count; i++)
                    options[i] = _animNames[i] + "  (" + _animUsages[i] + ")";
                int newIdx = EditorGUILayout.Popup("动画", _animIndex, options);
                if (newIdx != _animIndex) { _animIndex = newIdx; _animChanged = true; }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField("模型无动画", EditorStyles.miniLabel);
            }

            // 控制
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_playing ? "暂停" : "播放"))
                _playing = !_playing;
            if (GUILayout.Button("复位视角"))
            {
                _cameraEuler = Vector2.zero;
                _cameraPan = Vector2.zero;
                _cameraDist = 4f;
            }
            if (GUILayout.Button("重新加载"))
                LoadModel();
            EditorGUILayout.EndHorizontal();
        }

        private Bounds GetBounds()
        {
            if (_previewInstance == null) return new Bounds(Vector3.zero, Vector3.one);
            var renderers = _previewInstance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }

        private void HandleDrag(Rect rect)
        {
            if (!rect.Contains(Event.current.mousePosition)) return;

            if (Event.current.type == EventType.MouseDrag)
            {
                if (Event.current.button == 0)
                {
                    // 左键: 旋转
                    _cameraEuler.x += Event.current.delta.x;
                    _cameraEuler.y += Event.current.delta.y;
                    Event.current.Use();
                }
                else if (Event.current.button == 1 || Event.current.button == 2)
                {
                    // 右键/中键: 平移
                    _cameraPan.x += Event.current.delta.x * 0.01f;
                    _cameraPan.y += Event.current.delta.y * 0.01f;
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.ScrollWheel)
            {
                // 滚轮: 缩放
                _cameraDist += Event.current.delta.y * 0.5f;
                _cameraDist = Mathf.Clamp(_cameraDist, 1f, 20f);
                Event.current.Use();
            }
        }
    }
}
