using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Plugin
{
    public class TransformWindow : MonoBehaviour
    {
        private enum ResizeCorner { None, TopLeft, TopRight, BottomLeft, BottomRight }

        private const float DefaultWindowWidth = 420f;
        private const float DefaultWindowHeight = 560f;
        private const float MinWindowWidth = 320f;
        private const float MinWindowHeight = 400f;
        private const float DefaultTextAreaHeight = 80f;
        private const float ResizeHandleSize = 12f;
        private const float TitleBarHeight = 20f;
        // 标题栏 + 窗口皮肤上下内边距的估算高度，滚动视图的可视高度 = 窗口高度 - 这部分
        private const float ContentAreaPadding = 50f;

        private Rect _windowRect = new Rect(100, 100, DefaultWindowWidth, DefaultWindowHeight);
        private ResizeCorner _activeResizeCorner = ResizeCorner.None;
        private Vector2 _scrollPos = Vector2.zero;

        private string _inputCode = "";
        private string _outputCode = "";
        private string _statusMessage = "";
        private bool _statusIsError;

        private string _offsetX = "0";
        private string _offsetY = "0";
        private string _offsetZ = "0";
        private string _offsetIndex = "";
        private string _zoomX = "1";
        private string _zoomY = "1";
        private string _rotate = "0";

        private bool _flipH = false;
        private bool _flipV = false;

        // 脏状态基线快照
        private string _baselineOffsetX = "0";
        private string _baselineOffsetY = "0";
        private string _baselineOffsetZ = "0";
        private string _baselineOffsetIndex = "";
        private bool _baselineFlipH = false;
        private bool _baselineFlipV = false;
        private string _baselineZoomX = "1";
        private string _baselineZoomY = "1";
        private string _baselineRotate = "0";

        private BlueprintData? _parsed;

        // 双击标题栏时窗口恢复到的宽高，可由用户在窗口顶部的文本框里自行设置
        private string _defaultWidthText = "420";
        private string _defaultHeightText = "560";

        private float MaxWindowWidth => Screen.width / 2f;
        private float MaxWindowHeight => Screen.height / 2f;

        // 窗口变宽/变高时，输入输出文本框跟着放大；其余控件保持原有固定尺寸
        // 减去的量比 ContentAreaPadding 多留一点，给滚动视图的竖直滚动条腾出空间
        private float TextAreaWidth => Mathf.Max(200f, _windowRect.width - 45f);
        private float TextAreaHeight => DefaultTextAreaHeight + Mathf.Max(0f, _windowRect.height - DefaultWindowHeight) / 2f;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_inputCode))
                _inputCode = GUIUtility.systemCopyBuffer;
        }

        private void OnGUI()
        {
            HandleResize();
            // 必须显式传入 Width/Height，否则 GUILayout.Window 会按内容自动计算尺寸，
            // 把拖角/双击/设置按钮刚设好的 _windowRect 宽高覆盖掉
            _windowRect = GUILayout.Window(
                GetInstanceID(), _windowRect, DrawWindow, "蓝图变换",
                GUILayout.Width(_windowRect.width), GUILayout.Height(_windowRect.height));
        }

        private void HandleResize()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    if (e.clickCount == 2 && IsInTitleBar(e.mousePosition))
                    {
                        ApplyDefaultWindowSize();
                        e.Use();
                        break;
                    }
                    var corner = GetResizeCorner(e.mousePosition);
                    if (corner != ResizeCorner.None)
                    {
                        _activeResizeCorner = corner;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag when _activeResizeCorner != ResizeCorner.None:
                    ApplyResize(e.delta);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    _activeResizeCorner = ResizeCorner.None;
                    break;
            }
        }

        private bool IsInTitleBar(Vector2 mousePos)
            => mousePos.y >= _windowRect.y && mousePos.y <= _windowRect.y + TitleBarHeight
               && mousePos.x >= _windowRect.x && mousePos.x <= _windowRect.xMax;

        private void ApplyDefaultWindowSize()
        {
            float width = Mathf.Clamp((float)ParseOrZero(_defaultWidthText, DefaultWindowWidth), MinWindowWidth, MaxWindowWidth);
            float height = Mathf.Clamp((float)ParseOrZero(_defaultHeightText, DefaultWindowHeight), MinWindowHeight, MaxWindowHeight);
            _windowRect = new Rect(_windowRect.x, _windowRect.y, width, height);
        }

        private ResizeCorner GetResizeCorner(Vector2 mousePos)
        {
            bool nearLeft = mousePos.x >= _windowRect.x && mousePos.x <= _windowRect.x + ResizeHandleSize;
            bool nearRight = mousePos.x <= _windowRect.xMax && mousePos.x >= _windowRect.xMax - ResizeHandleSize;
            bool nearTop = mousePos.y >= _windowRect.y && mousePos.y <= _windowRect.y + ResizeHandleSize;
            bool nearBottom = mousePos.y <= _windowRect.yMax && mousePos.y >= _windowRect.yMax - ResizeHandleSize;

            if (nearLeft && nearTop) return ResizeCorner.TopLeft;
            if (nearRight && nearTop) return ResizeCorner.TopRight;
            if (nearLeft && nearBottom) return ResizeCorner.BottomLeft;
            if (nearRight && nearBottom) return ResizeCorner.BottomRight;
            return ResizeCorner.None;
        }

        private void ApplyResize(Vector2 delta)
        {
            bool left = _activeResizeCorner is ResizeCorner.TopLeft or ResizeCorner.BottomLeft;
            bool top = _activeResizeCorner is ResizeCorner.TopLeft or ResizeCorner.TopRight;

            float newX = _windowRect.x;
            float newY = _windowRect.y;
            float newWidth = _windowRect.width;
            float newHeight = _windowRect.height;

            if (left) { newWidth -= delta.x; newX += delta.x; }
            else newWidth += delta.x;

            if (top) { newHeight -= delta.y; newY += delta.y; }
            else newHeight += delta.y;

            float clampedWidth = Mathf.Clamp(newWidth, MinWindowWidth, MaxWindowWidth);
            float clampedHeight = Mathf.Clamp(newHeight, MinWindowHeight, MaxWindowHeight);
            if (left) newX -= clampedWidth - newWidth;
            if (top) newY -= clampedHeight - newHeight;

            _windowRect = new Rect(newX, newY, clampedWidth, clampedHeight);
        }

        private void DrawWindow(int id)
        {
            float viewportHeight = Mathf.Max(50f, _windowRect.height - ContentAreaPadding);
            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(viewportHeight));

            // ---- 窗口默认宽高 ----
            GUILayout.Label("窗口默认宽高（双击标题栏应用，或点设置立即生效）");
            GUILayout.BeginHorizontal();
            GUILayout.Label("宽度", GUILayout.Width(30));
            _defaultWidthText = GUILayout.TextField(_defaultWidthText, GUILayout.Width(50));
            GUILayout.Label("高度", GUILayout.Width(30));
            _defaultHeightText = GUILayout.TextField(_defaultHeightText, GUILayout.Width(50));
            if (GUILayout.Button("设置")) ApplyDefaultWindowSize();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            // ---- 蓝图码输入 ----
            GUILayout.Label("蓝图码（粘贴或从剪贴板读取）");
            _inputCode = GUILayout.TextArea(_inputCode, GUILayout.Height(TextAreaHeight), GUILayout.Width(TextAreaWidth));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("从剪贴板读取")) _inputCode = GUIUtility.systemCopyBuffer;
            if (GUILayout.Button("解析")) TryParse();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                Color prevColor = GUI.color;
                GUI.color = _statusIsError ? Color.red : Color.green;
                GUILayout.Label(_statusMessage);
                GUI.color = prevColor;
            }

            GUILayout.Space(8);

            // ---- 偏移：X/Y/Z 同行 ----
            GUILayout.Label("偏移");
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(12));
            _offsetX = GUILayout.TextField(_offsetX, GUILayout.Width(60));
            GUILayout.Label("Y", GUILayout.Width(12));
            _offsetY = GUILayout.TextField(_offsetY, GUILayout.Width(60));
            GUILayout.Label("Z", GUILayout.Width(12));
            _offsetZ = GUILayout.TextField(_offsetZ, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            // ---- 水平翻转：checkbox ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("水平翻转", GUILayout.Width(60));
            _flipH = GUILayout.Toggle(_flipH, "横向");
            _flipV = GUILayout.Toggle(_flipV, "纵向");
            GUILayout.EndHorizontal();

            // ---- 线性变换：横向/纵向同行，旋转另起一行 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("线性变换", GUILayout.Width(60));
            GUILayout.Label("横向", GUILayout.Width(30));
            _zoomX = GUILayout.TextField(_zoomX, GUILayout.Width(60));
            GUILayout.Label("纵向", GUILayout.Width(30));
            _zoomY = GUILayout.TextField(_zoomY, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("旋转角度(-360~360)", GUILayout.Width(130));
            _rotate = GUILayout.TextField(_rotate, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            // ---- 传送带序号 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label("传送带序号(留空=全部)", GUILayout.Width(150));
            _offsetIndex = GUILayout.TextField(_offsetIndex);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // ---- 应用 / 重置 ----
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("应用")) ApplyAll();
            if (GUILayout.Button("重置")) ResetAll();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // ---- 输出蓝图码 ----
            GUILayout.Label("输出蓝图码");
            GUILayout.TextArea(_outputCode, GUILayout.Height(TextAreaHeight), GUILayout.Width(TextAreaWidth));
            if (GUILayout.Button("复制到剪贴板")) GUIUtility.systemCopyBuffer = _outputCode;

            GUILayout.EndScrollView();

            GUI.DragWindow();
            DrawResizeHandles();
        }

        private static readonly Color ResizeHandleColor = new Color(1f, 1f, 1f, 0.35f);

        private void DrawResizeHandles()
        {
            Color prevColor = GUI.color;
            GUI.color = ResizeHandleColor;
            GUI.Box(new Rect(0, 0, ResizeHandleSize, ResizeHandleSize), GUIContent.none);
            GUI.Box(new Rect(_windowRect.width - ResizeHandleSize, 0, ResizeHandleSize, ResizeHandleSize), GUIContent.none);
            GUI.Box(new Rect(0, _windowRect.height - ResizeHandleSize, ResizeHandleSize, ResizeHandleSize), GUIContent.none);
            GUI.Box(new Rect(_windowRect.width - ResizeHandleSize, _windowRect.height - ResizeHandleSize, ResizeHandleSize, ResizeHandleSize), GUIContent.none);
            GUI.color = prevColor;
        }

        private void TryParse()
        {
            try
            {
                _parsed = BlueprintParser.FromStr(_inputCode);
                _statusMessage = $"解析成功：{_parsed.Buildings.Count} 个建筑";
                _statusIsError = false;
                CaptureBaseline();
            }
            catch (Exception ex)
            {
                _parsed = null;
                _statusMessage = $"解析失败：{ex.Message}";
                _statusIsError = true;
            }
        }

        private bool IsBeltOnlySmall()
            => _parsed != null
               && _parsed.Buildings.Count > 0
               && _parsed.Buildings.Count < 20
               && _parsed.Buildings.All(b => BuildingMeta.IsBelt(b.ItemId));

        private void ApplyAll()
        {
            if (!EnsureParsed()) return;

            bool anyDirty = false;
            var data = _parsed!.Clone();

            if (IsOffsetDirty())
            {
                double x = ParseOrZero(_offsetX);
                double y = ParseOrZero(_offsetY);
                double z = ParseOrZero(_offsetZ);
                var indices = ParseBeltIndices(_offsetIndex);

                // 指定传送带序号时需满足全为传送带且数量<20（与网页版限制一致；spec 开放问题待定）
                if (indices != null && !IsBeltOnlySmall())
                {
                    _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号";
                    _statusIsError = true;
                    return;
                }

                data = BlueprintTransform.HorizontalOffset(data, x, y, indices);
                if (z != 0)
                {
                    var afterVert = BlueprintTransform.VerticalOffset(data, z, indices);
                    bool addedBase = afterVert.Buildings.Count > data.Buildings.Count;
                    data = afterVert;
                    if (addedBase) _statusMessage = "检测到悬空建筑，已自动加地基";
                }
                anyDirty = true;
            }

            if (IsFlipDirty())
            {
                double zoomX = _flipH ? -1 : 1;
                double zoomY = _flipV ? -1 : 1;
                data = BlueprintTransform.LinearTransformation(data, zoomX, zoomY, 0);
                anyDirty = true;
            }

            if (IsLinearDirty())
            {
                double zoomX = ParseOrZero(_zoomX, 1);
                double zoomY = ParseOrZero(_zoomY, 1);
                double rotate = ParseOrZero(_rotate, 0);
                data = BlueprintTransform.LinearTransformation(data, zoomX, zoomY, rotate);
                anyDirty = true;
            }

            if (!anyDirty)
            {
                _statusMessage = "无变更";
                _statusIsError = true;
                return;
            }

            _parsed = data;
            _outputCode = BlueprintParser.ToStr(data);
            GUIUtility.systemCopyBuffer = _outputCode;
            if (string.IsNullOrEmpty(_statusMessage))
                _statusMessage = "已应用变换并复制到剪贴板";
            _statusIsError = false;
            CaptureBaseline();
        }

        private void ResetAll()
        {
            _offsetX = "0";
            _offsetY = "0";
            _offsetZ = "0";
            _offsetIndex = "";
            _flipH = false;
            _flipV = false;
            _zoomX = "1";
            _zoomY = "1";
            _rotate = "0";
            _outputCode = "";
            _statusMessage = "";
            _statusIsError = false;
            _parsed = null;
        }

        private bool EnsureParsed()
        {
            if (_parsed != null) return true;
            _statusMessage = "请先粘贴蓝图码并点击「解析」";
            _statusIsError = true;
            return false;
        }

        private void Finish(BlueprintData result, string? extraNote = null)
        {
            _parsed = result;
            _outputCode = BlueprintParser.ToStr(result);
            GUIUtility.systemCopyBuffer = _outputCode;
            _statusMessage = "已应用变换并复制到剪贴板";
            if (!string.IsNullOrEmpty(extraNote))
                _statusMessage += $"（{extraNote}）";
            _statusIsError = false;
        }

        private static double ParseOrZero(string s, double fallback = 0)
            => double.TryParse(s, out var v) ? v : fallback;

        private static HashSet<int>? ParseBeltIndices(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var set = new HashSet<int>();
            foreach (var part in s.Split(','))
            {
                if (int.TryParse(part.Trim(), out var idx))
                    set.Add(idx);
            }
            return set.Count > 0 ? set : null;
        }

        private void CaptureBaseline()
        {
            _baselineOffsetX = _offsetX;
            _baselineOffsetY = _offsetY;
            _baselineOffsetZ = _offsetZ;
            _baselineOffsetIndex = _offsetIndex;
            _baselineFlipH = _flipH;
            _baselineFlipV = _flipV;
            _baselineZoomX = _zoomX;
            _baselineZoomY = _zoomY;
            _baselineRotate = _rotate;
        }

        private bool IsOffsetDirty()
            => _offsetX != _baselineOffsetX
            || _offsetY != _baselineOffsetY
            || _offsetZ != _baselineOffsetZ
            || _offsetIndex != _baselineOffsetIndex;

        private bool IsFlipDirty()
            => _flipH != _baselineFlipH || _flipV != _baselineFlipV;

        private bool IsLinearDirty()
            => _zoomX != _baselineZoomX || _zoomY != _baselineZoomY || _rotate != _baselineRotate;
    }
}
