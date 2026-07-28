using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MapChangeManager : MonoBehaviour
{
    [NaughtyAttributes.Scene] public string currentMap;
    [NaughtyAttributes.Scene] public string mapToGo;

    public bool trig;
    private float time;

    // 씬 전환이 완전히 끝난 시점(플레이어가 MapStartPos로 배치된 뒤)에 발행 — 세이브 로드가
    // "저장된 맵으로 전환 후 저장된 좌표로 텔레포트"를 하려면 로드 완료 시점을 알아야 하는데,
    // ChangeMap()이 내부적으로 코루틴이라 완료를 동기적으로 기다릴 방법이 없어서 필요하다.
    public event Action<string> OnMapChanged;

    public void ChangeMap(string mapName)
    {
        GameManager.instance.chunkManager.UnregisterChunkDataAll();
        if (currentMap != null)
        {
            AsyncOperation unloadOp = SceneManager.UnloadSceneAsync(currentMap);
            unloadOp.completed += (op) => StartCoroutine(LoadMap(mapName));
        }
        else
            StartCoroutine(LoadMap(mapName));
    }

    private IEnumerator LoadMap(string mapName)
    {
        AsyncOperation loadOp = SceneManager.LoadSceneAsync(mapName, LoadSceneMode.Additive);
        time = Time.time;
        while (!loadOp.isDone)
        {
            LoadingProgress(loadOp.progress);
            yield return null;
        }
        yield return null;
        AfterLoadMap(mapName);
    }

    private void AfterLoadMap(string mapName)
    {
        Debug.Log(mapName);
        MapStartPos[] startPoses = FindObjectsOfType<MapStartPos>();
        for (int i = 0; i < startPoses.Length; i++)
        {
            if (startPoses[i].fromScene.Equals(currentMap))
            {
                startPoses[i].SetPlayerToStartPos();
            }

        }
        currentMap = mapName;
        //GameManager.instance.chunkManager.RegisterChunkDataAll();
        OnMapChanged?.Invoke(mapName);
    }

    private void LoadingProgress(float progress)
    {
        Debug.Log(progress);
    }

    // Update is called once per frame
    void Update()
    {
        if (trig) ChangeMap(mapToGo);
        trig = false;
    }
}
