using UnityEditor;
using UnityEngine;

/// <summary>
/// BuffableModule 인스펙터 — 그리기는 전부 기본 인스펙터에 맡기고, 리페인트 주기만 관리한다.
///
/// CurrentBuffs 각 항목의 남은 시간은 BuffInfoDrawer가 그린다. 그 값(Timer.RemainTime)은
/// DOTween 재생 시계를 실시간으로 읽는 값이라, 인스펙터를 다시 그리지 않으면 숫자가 멈춰 보인다.
/// 그렇다고 RequiresConstantRepaint()로 매 프레임 다시 그리면 인스펙터가 눈에 띄게 버벅여서,
/// 초 단위 표시에 충분한 만큼만 주기적으로 Repaint()한다.
/// </summary>
[CustomEditor(typeof(BuffableModule))]
[CanEditMultipleObjects]
public class BuffableModuleEditor : Editor
{
    private const double RepaintInterval = 0.5;

    private double _nextRepaint;

    private void OnEnable() => EditorApplication.update += ThrottledRepaint;
    private void OnDisable() => EditorApplication.update -= ThrottledRepaint;

    private void ThrottledRepaint()
    {
        if (!Application.isPlaying) return;
        if (EditorApplication.timeSinceStartup < _nextRepaint) return;

        _nextRepaint = EditorApplication.timeSinceStartup + RepaintInterval;
        Repaint();
    }
}
