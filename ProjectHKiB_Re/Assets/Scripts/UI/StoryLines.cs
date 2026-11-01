using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 임시 스토리 연출: 화면 중앙에 줄을 하나씩 띄우고 확인(Enter)마다 넘긴 뒤, 끝나면 다음 맵으로 간다.
/// </summary>
public class StoryLines : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI text;
    [SerializeField, TextArea] private string[] lines;
    [SerializeField] private MapDataSO nextMap;
    [SerializeField] private float fadeSec = 0.5f;
    // 글자가 찍히는 동안만 반복 재생하는 타이핑 소리(루프 AudioSource). 다 찍히면(또는 Enter로 완성하면) 멈춘다.
    [SerializeField] private AudioSource typingSource;

    private int _index;
    private Tweener _typing;

    private void Start()
    {
        GameManager.instance.inputManager.MENUMode(); // 확인 입력은 MENU 맵에 있고, 그동안 플레이어는 못 움직인다
        GameManager.instance.inputManager.onSubmit += OnSubmit;
        if (lines.Length > 0) Print(lines[0]);
    }

    private void OnDestroy()
    {
        _typing?.Kill();
        text.DOKill(); // 글자 페이드 인 도중 씬이 내려가면 DOTween이 "대상 없음" 경고를 남긴다
        if (GameManager.instance != null) GameManager.instance.inputManager.onSubmit -= OnSubmit;
    }

    // DialogueModule.PrintCurrentSubLine과 같은 타이핑: 글자당 0.1초, 게임이 멈춰도 흐르도록 unscaled.
    private const string FadeInMark = "(페이드 인)";

    private void Print(string line)
    {
        _typing?.Kill();
        text.text = "";

        // 줄 앞에 "(페이드 인)"을 붙이면 그 화면은 글자가 서서히 나타난다(새 기록이 시작되는 곳).
        text.DOKill();
        text.alpha = 1f;
        if (line.StartsWith(FadeInMark))
        {
            line = line.Substring(FadeInMark.Length).TrimStart('\n');
            text.alpha = 0f;
            text.DOFade(1f, fadeSec).SetUpdate(true);
        }

        _typing = text.DOText(line, line.Length * 0.1f).SetEase(Ease.Linear).SetUpdate(true)
            .OnComplete(typingSource.Stop);
        typingSource.Play();
    }

    private void OnSubmit(InputAction.CallbackContext context)
    {
        if (!context.started) return;

        // 타이핑 중이면 이번 Enter는 줄을 바로 완성만 한다.
        if (_typing != null && _typing.IsActive() && _typing.IsPlaying())
        {
            _typing.Complete();
            return;
        }

        _index++;
        if (_index < lines.Length)
        {
            Print(lines[_index]);
            return;
        }

        GameManager.instance.inputManager.onSubmit -= OnSubmit;
        text.text = "";
        // 화면을 검게 덮은 뒤 다음 맵을 부른다. 다시 밝히는 건 맵 로드가 끝난 MapManager가 한다.
        ScreenEffectManager.Instance.FadeToBlack(fadeSec);
        DOVirtual.DelayedCall(fadeSec, () =>
        {
            GameManager.instance.inputManager.PLAYMode();
            // Story 맵엔 바닥이 없어 대사 동안 플레이어가 계속 떨어진다. 시작 지점 배치는 위치만 옮기므로
            // 낙하 속도가 남으면 다음 맵 바닥에 닿자마자 위로 튕긴다 — 넘어가기 전에 지운다.
            FindObjectOfType<PhysicsManager>().RemoveVelocity(GameManager.instance.player.GetInterface<IPhysics>());
            GameManager.instance.mapManager.LoadMap(nextMap);
        }, true);
    }
}
