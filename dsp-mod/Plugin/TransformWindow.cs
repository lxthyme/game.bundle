using System;
using UnityEngine;
using DspBlueprintTransform.Blueprint;

namespace DspBlueprintTransform.Plugin
{
    public class TransformWindow : MonoBehaviour
    {
        private Rect _windowRect = new Rect(100, 100, 420, 560);
        private string _inputCode = "";
        private string _outputCode = "";
        private string _statusMessage = "";
        private bool _statusIsError;

        private string _offsetX = "0";
        private string _offsetY = "0";
        private string _offsetZ = "0";
        private string _zoomX = "1";
        private string _zoomY = "1";
        private string _rotate = "0";

        private BlueprintData? _parsed;

        private void OnGUI()
        {
            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, "蓝图变换");
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label("蓝图码（粘贴或从剪贴板读取）");
            _inputCode = GUILayout.TextArea(_inputCode, GUILayout.Height(80));

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
            GUILayout.TextArea(_outputCode, GUILayout.Height(80));
            if (GUILayout.Button("复制到剪贴板")) GUIUtility.systemCopyBuffer = _outputCode;

            GUI.DragWindow();
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
            double x = ParseOrZero(_offsetX);
            double y = ParseOrZero(_offsetY);
            double z = ParseOrZero(_offsetZ);
            var afterHorizontal = BlueprintTransform.HorizontalOffset(_parsed!, x, y);
            var afterVertical = BlueprintTransform.VerticalOffset(afterHorizontal, z);
            bool addedBase = afterVertical.Buildings.Count > afterHorizontal.Buildings.Count;
            Finish(afterVertical, addedBase ? "检测到悬空建筑，已自动加地基" : null);
        }

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
