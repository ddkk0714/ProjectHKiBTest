using System.Collections;
using DG.Tweening;
using UnityEngine;

public class FaceController : MonoBehaviour
{
    public RectTransform rectTransform;
    public Animator animator;
    private Sequence mouthTween;
    private Sequence sayingTween;
    private Sequence eyeTween;
    public Transform[] mouth_Normal;
    public Transform[] mouth_M;
    public Transform[] mouth_A;
    public Transform[] mouth_O;
    public Transform[] mouth_E;
    public Transform[] eye_Open;
    public Transform[] eye_Close;

    public bool blinkLoop = true;
    private bool blinkInProgress;

    [NaughtyAttributes.ResizableTextArea]
    public string testPhraseCode;
    [NaughtyAttributes.Button()]
    public void SayPhrase() => SayPhrase(testPhraseCode);

    private void SetActives(Transform[] transforms, bool set)
    {
        for (int i = 0; i < transforms.Length; i++) transforms[i].gameObject.SetActive(set);
    }

    public void PlayAnimation(string name)
    {
        if (animator) animator.Play(name);
    }

    public void SayPhrase(string code)
    {
        StopSaying();
        // SetUpdate(true): 초상화 연출은 대화창과 함께 쓰이므로 TimeManager가 게임을
        // 멈춘 동안에도 계속 움직여야 한다. (아래 입/눈 트윈도 동일)
        sayingTween = DOTween.Sequence().SetUpdate(true);
        char prevCode = 'S';
        for (int i = 0; i < code.Length; i++)
        {
            float interval = 0.12f;
            if (code[i] == 'S') { prevCode = 'S'; interval = 0; } // Skip

            if (code[i] == '_') { prevCode = '_'; } // Space
            if (code[i] == 'M') { sayingTween.AppendCallback(SayM); prevCode = 'M'; interval = 0.06f; } // M sound
            if (code[i] == 'O') { sayingTween.AppendCallback(SayO); prevCode = 'O'; } // O sound
            if (code[i] == 'A') { sayingTween.AppendCallback(SayA); prevCode = 'A'; } // A sound
            if (code[i] == 'E') { sayingTween.AppendCallback(SayE); prevCode = 'E'; } // E sound
            if (code[i] == 'L') // Link to previous sound
            {
                if (prevCode == 'M') { sayingTween.AppendCallback(SayM); prevCode = 'M'; interval = 0.06f; }
                if (prevCode == 'O') { sayingTween.AppendCallback(SayO); prevCode = 'O'; }
                if (prevCode == 'A') { sayingTween.AppendCallback(SayA); prevCode = 'A'; }
                if (prevCode == 'E') { sayingTween.AppendCallback(SayA); prevCode = 'E'; }
            }
            sayingTween.AppendInterval(interval);
        }
        sayingTween.Play();
    }

    [NaughtyAttributes.Button()]
    public void StopSaying() => sayingTween?.Complete();

    [NaughtyAttributes.Button()]
    public void SayM()
    {
        mouthTween?.Complete();
        SetActives(mouth_Normal, false);
        SetActives(mouth_M, true);
        mouthTween = DOTween.Sequence().SetUpdate(true);
        mouthTween.AppendInterval(0.25f);
        mouthTween.OnComplete(SayEnd);
        mouthTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void SayA()
    {
        mouthTween?.Complete();
        SetActives(mouth_Normal, false);
        SetActives(mouth_A, true);
        mouthTween = DOTween.Sequence().SetUpdate(true);
        mouthTween.AppendInterval(0.25f);
        mouthTween.OnComplete(SayEnd);
        mouthTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void SayO()
    {
        mouthTween?.Complete();
        SetActives(mouth_Normal, false);
        SetActives(mouth_O, true);
        mouthTween = DOTween.Sequence().SetUpdate(true);
        mouthTween.AppendInterval(0.25f);
        mouthTween.OnComplete(SayEnd);
        mouthTween.Play();
    }
    [NaughtyAttributes.Button()]
    public void SayE()
    {
        mouthTween?.Complete();
        SetActives(mouth_Normal, false);
        SetActives(mouth_E, true);
        mouthTween = DOTween.Sequence().SetUpdate(true);
        mouthTween.AppendInterval(0.25f);
        mouthTween.OnComplete(SayEnd);
        mouthTween.Play();
    }

    public void SayEnd()
    {
        SetActives(mouth_O, false);
        SetActives(mouth_A, false);
        SetActives(mouth_M, false);
        SetActives(mouth_E, false);
        SetActives(mouth_Normal, true);
    }

    public void Update()
    {
        if (blinkLoop && !blinkInProgress)
            StartCoroutine(BlinkLoop());
    }

    public IEnumerator BlinkLoop()
    {
        blinkInProgress = true;
        float interval = UnityEngine.Random.value > 0.05f ? UnityEngine.Random.Range(4f, 10f) : 0.2f;
        // WaitForSeconds는 스케일된 시간이라 일시정지 중 눈 깜빡임이 멈춘다. Realtime을 쓴다.
        yield return new WaitForSecondsRealtime(interval);
        Blink();
        blinkInProgress = false;
    }

    [NaughtyAttributes.Button()]
    public void Blink()
    {
        eyeTween?.Complete();
        SetActives(eye_Close, true);
        SetActives(eye_Open, false);
        eyeTween = DOTween.Sequence().SetUpdate(true);
        eyeTween.AppendInterval(0.12f);
        eyeTween.OnComplete(BlinkEnd);
        eyeTween.Play();
    }

    public void BlinkEnd()
    {
        SetActives(eye_Close, false);
        SetActives(eye_Open, true);
    }

    public void ChangeExpression(string animationName)
    {
        animator.Play(animationName);
    }
}
