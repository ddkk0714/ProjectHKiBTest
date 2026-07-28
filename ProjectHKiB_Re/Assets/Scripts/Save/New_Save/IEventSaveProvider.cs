using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IEventSaveProvider
{
    // 세이브 파일 안에서 이 provider의 항목들을 다른 provider와 구분해 담아두는 안정적 키
    // (예: "RouteModule", "EventManager"). 여러 provider가 합성될 때 서로 다른 ID 체계
    // (RouteModule은 "mapGuid:eventKey", EventManager는 EventFlagSO.Id 즉 에셋 GUID)를 쓰므로,
    // 세이브 파일에서 한 리스트에 섞어 넣지 않고 provider별로 스코프를 나누기 위해 필요하다.
    string ProviderId { get; }

    Dictionary<string, int> EventFlags { get; }
    void SetEventFlag(string id, int value);

    Dictionary<string, bool> Passages { get; }
    void SetPassage(string id, bool opened);

    // 로드 시작 시 SaveModule.LoadEvents()가 SetEventFlag/SetPassage를 항목별로 호출하기 전에
    // 1회 호출한다. 항목이 개별 호출로 오기 때문에 "이전 상태를 지우고 새로 시작"할 시점을
    // 구현체가 직접 알 방법이 없어서 필요하다 — 이 훅이 없으면 이전 상태(또는 기본값)와
    // 새로 로드되는 값이 섞일 수 있다.
    void ResetForLoad();
}
