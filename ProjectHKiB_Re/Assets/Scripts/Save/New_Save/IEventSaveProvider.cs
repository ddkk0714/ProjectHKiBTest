using System.Collections;
using System.Collections.Generic;   
using UnityEngine;

public interface IEventSaveProvider
{
    Dictionary<string, bool> EventFlags { get; }
    void SetEventFlag(string id, bool value);

    Dictionary<string, bool> Passages { get; }
    void SetPassage(string id, bool opened);

    // 로드 시작 시 SaveModule.LoadEvents()가 SetEventFlag/SetPassage를 항목별로 호출하기 전에
    // 1회 호출한다. 항목이 개별 호출로 오기 때문에 "이전 상태를 지우고 새로 시작"할 시점을
    // 구현체가 직접 알 방법이 없어서 필요하다 — 이 훅이 없으면 이전 상태(또는 기본값)와
    // 새로 로드되는 값이 섞일 수 있다.
    void ResetForLoad();
}

