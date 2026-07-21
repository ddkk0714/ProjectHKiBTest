using System;

// 지도(M)/노트(V)/도감(C)처럼 단축키로 토글되는 풀스크린 패널들이 동시에 여러 개 열리지 않도록
// 조율하는 정적 헬퍼 — 한 패널이 열릴 때 다른 패널이 이미 열려 있으면 그 패널을 먼저 닫는다.
// MapViewer가 노트/도감의 존재를 몰라도 되는 기존 설계(GoToNote/GoToCodex가 씬에서 찾아 호출하는
// 방식)와 같은 방향으로, 패널들이 서로를 직접 참조하지 않고 이 정적 클래스를 통해서만 조율한다.
public static class ExclusivePanelGroup
{
    private static object _openOwner;
    private static Action _openOwnerClose;

    // 패널이 Open() 안에서, 실제로 활성화하기 직전(유효성 검사 통과 후)에 호출한다.
    // 이미 다른 패널이 열려 있으면 그 패널의 Close 콜백을 먼저 호출해 닫는다.
    public static void NotifyOpening(object owner, Action closeSelf)
    {
        if (_openOwner != null && _openOwner != owner)
            _openOwnerClose?.Invoke();

        _openOwner = owner;
        _openOwnerClose = closeSelf;
    }

    // 패널이 Close() 안에서 호출한다. 지금 기록된 소유자가 자기 자신일 때만 지운다 — 이미 다른
    // 패널이 새로 열리며 자신을 닫은 경우(NotifyOpening이 먼저 갱신함)까지 잘못 지우지 않기 위함.
    public static void NotifyClosing(object owner)
    {
        if (_openOwner == owner)
        {
            _openOwner = null;
            _openOwnerClose = null;
        }
    }
}
