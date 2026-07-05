using System;
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

        private Rect _windowRect = new Rect(100, 100, DefaultWindowWidth, DefaultWindowHeight);
        private ResizeCorner _activeResizeCorner = ResizeCorner.None;

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

        private BlueprintData? _parsed;

        // 窗口变宽/变高时，输入输出文本框跟着放大；其余控件保持原有固定尺寸
        private float TextAreaWidth => Mathf.Max(200f, _windowRect.width - 30f);
        private float TextAreaHeight => DefaultTextAreaHeight + Mathf.Max(0f, _windowRect.height - DefaultWindowHeight) / 2f;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_inputCode))
                _inputCode = GUIUtility.systemCopyBuffer;
        }

        private void OnGUI()
        {
            HandleResize();
            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, "蓝图变换");
        }

        private void HandleResize()
        {
            Event e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
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

            float clampedWidth = Mathf.Max(newWidth, MinWindowWidth);
            float clampedHeight = Mathf.Max(newHeight, MinWindowHeight);
            if (left) newX -= clampedWidth - newWidth;
            if (top) newY -= clampedHeight - newHeight;

            _windowRect = new Rect(newX, newY, clampedWidth, clampedHeight);
        }

        private void DrawWindow(int id)
        {
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
            GUILayout.Label("坐标偏移");
            DrawLabeledField("横向偏移 X", ref _offsetX);
            DrawLabeledField("纵向偏移 Y", ref _offsetY);
            DrawLabeledField("垂直偏移 Z", ref _offsetZ);
            DrawLabeledField("传送带序号(留空=全部)", ref _offsetIndex);
            if (GUILayout.Button("应用坐标偏移")) ApplyOffset();

            GUILayout.Space(8);
            GUILayout.Label("水平翻转");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("横向翻转")) ApplyLinearTransformation(-1, 1, 0);
            if (GUILayout.Button("纵向翻转")) ApplyLinearTransformation(1, -1, 0);
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label("线性变换");
            DrawLabeledField("横向缩放量", ref _zoomX);
            DrawLabeledField("纵向缩放量", ref _zoomY);
            DrawLabeledField("旋转角度(-360~360)", ref _rotate);
            if (GUILayout.Button("应用线性变换")) ApplyLinearTransformationFromFields();

            GUILayout.Space(8);
            GUILayout.Label("输出蓝图码");
            GUILayout.TextArea(_outputCode, GUILayout.Height(TextAreaHeight), GUILayout.Width(TextAreaWidth));
            if (GUILayout.Button("复制到剪贴板")) GUIUtility.systemCopyBuffer = _outputCode;

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

        private static void DrawLabeledField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(150));
            value = GUILayout.TextField(value);
            GUILayout.EndHorizontal();
        }

        private void TryParse()
        {
            try
            {
                _parsed = BlueprintParser.FromStr(_inputCode);
                _statusMessage = $"解析成功：{_parsed.Buildings.Count} 个建筑";
                _statusIsError = false;
            }
            catch (Exception ex)
            {
                _parsed = null;
                _statusMessage = $"解析失败：{ex.Message}";
                _statusIsError = true;
            }
        }

        private void ApplyOffset()
        {
            if (!EnsureParsed()) return;

            int targetIndex = -1;
            if (!string.IsNullOrWhiteSpace(_offsetIndex))
            {
                if (!IsBeltOnlySmall())
                {
                    _statusMessage = "仅当蓝图全部为传送带且数量小于 20 时可指定序号";
                    _statusIsError = true;
                    return;
                }
                if (!int.TryParse(_offsetIndex, out targetIndex) || targetIndex < 0 || targetIndex >= _parsed!.Buildings.Count)
                {
                    _statusMessage = "传送带序号无效";
                    _statusIsError = true;
                    return;
                }
            }

            double x = ParseOrZero(_offsetX);
            double y = ParseOrZero(_offsetY);
            double z = ParseOrZero(_offsetZ);
            var afterHorizontal = BlueprintTransform.HorizontalOffset(_parsed!, x, y, targetIndex);
            if (z == 0)
            {
                Finish(afterHorizontal);
                return;
            }
            var afterVertical = BlueprintTransform.VerticalOffset(afterHorizontal, z, targetIndex);
            bool addedBase = afterVertical.Buildings.Count > afterHorizontal.Buildings.Count;
            Finish(afterVertical, addedBase ? "检测到悬空建筑，已自动加地基" : null);
        }

        private bool IsBeltOnlySmall()
            => _parsed != null
               && _parsed.Buildings.Count > 0
               && _parsed.Buildings.Count < 20
               && _parsed.Buildings.All(b => BuildingMeta.IsBelt(b.ItemId));

        private void ApplyLinearTransformationFromFields()
        {
            if (!EnsureParsed()) return;
            double zoomX = ParseOrZero(_zoomX, 1);
            double zoomY = ParseOrZero(_zoomY, 1);
            double rotate = ParseOrZero(_rotate, 0);
            ApplyLinearTransformation(zoomX, zoomY, rotate);
        }

        private void ApplyLinearTransformation(double zoomX, double zoomY, double rotate)
        {
            if (!EnsureParsed()) return;
            var result = BlueprintTransform.LinearTransformation(_parsed!, zoomX, zoomY, rotate);
            Finish(result);
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
    }
}
