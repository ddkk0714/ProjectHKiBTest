using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using GraphProcessor;
using UnityEngine;

[NodeCustomEditor(typeof(StateNode))]
public class StateNodeView : BaseNodeView
{
    /// <summary>
    /// 노드마다 StateSO 인스펙터를 하나씩 들고 있다. 이걸 IMGUIContainer로 넣으면 그래프 창이
    /// 리페인트될 때마다(팬·줌·드래그 전부 포함) 노드 수만큼 인스펙터가 다시 그려져서 버벅인다.
    ///
    /// packed 상태에서 그릴 것은 노출 변수 몇 개와 Unpack 버튼뿐이므로, 그때는 IMGUI 대신
    /// UIElements(PropertyField + 바인딩)로 만든다. 바인딩은 값이 바뀔 때만 일하므로
    /// 리페인트 비용이 사라진다. unpacked일 때만 기존 StateSOEditor를 IMGUI로 띄운다 —
    /// Pack / Add Variable / Make Template / Load Template이 전부 IMGUI로 짜여 있기 때문이다.
    /// </summary>
    private Editor cachedEditor;
    private VisualElement inspectorRoot;

    public override void Enable()
    {
        style.width = 400;

        var stateNode = nodeTarget as StateNode;

        if (stateNode == null || stateNode.stateSO == null)
        {
            controlsContainer.Add(new Label("StateSO is not current."));
            return;
        }

        // UpdateAllPorts()는 엣지를 타고 연결된 노드까지 포트를 다시 만든다(릴레이 노드처럼 포트
        // 타입이 전파되는 그래프용). StateNode의 포트는 자기 stateSO.transitions만 보고 만들어지므로
        // 그 전파는 전부 헛일이고, 엣지가 100개 넘는 기계에서는 한 번 누를 때마다 그래프 전체가 멈칫한다.
        // Local 버전이 같은 결과를 낸다.
        Button refreshButton = new(() => { stateNode.UpdateAllPortsLocal(); })
        {
            text = "Refresh Transition Ports"
        };
        refreshButton.style.backgroundColor = new StyleColor(new Color(0.4f, 0.4f, 0.4f));
        controlsContainer.Add(refreshButton);

        inspectorRoot = new VisualElement();
        controlsContainer.Add(inspectorRoot);

        BuildInspector(stateNode.stateSO);
    }

    /// <summary>노드 뷰가 패널에서 떨어질 때 호출된다. 만들어 둔 Editor를 여기서 버려야 누수가 없다.</summary>
    public override void Disable()
    {
        DestroyEditor();
    }

    private void DestroyEditor()
    {
        if (cachedEditor == null) return;

        UnityEngine.Object.DestroyImmediate(cachedEditor);
        cachedEditor = null;
    }

    /// <summary>Pack/Unpack을 누르면 다시 불러 모드를 갈아탄다.</summary>
    private void BuildInspector(StateSO stateSO)
    {
        DestroyEditor();
        inspectorRoot.Clear();

        if (stateSO.isPacked)
        {
            BuildPackedInspector(stateSO);
            return;
        }

        cachedEditor = Editor.CreateEditor(stateSO);

        var imgui = new IMGUIContainer(() =>
        {
            if (cachedEditor == null || cachedEditor.target == null) return;

            cachedEditor.OnInspectorGUI();

            // Pack 버튼을 눌렀으면 다음 프레임에 UIElements 쪽으로 넘긴다.
            // (여기서 바로 바꾸면 그리는 도중에 자기 자신을 지우게 된다)
            if (stateSO.isPacked) schedule.Execute(() => BuildInspector(stateSO));
        })
        {
            cullingEnabled = true // 화면 밖으로 나간 노드는 아예 그리지 않는다
        };

        inspectorRoot.Add(imgui);
    }

    private void BuildPackedInspector(StateSO stateSO)
    {
        var serialized = new SerializedObject(stateSO);

        foreach (ExposedVariable exposed in stateSO.exposedVariables)
        {
            SerializedProperty prop = serialized.FindProperty(exposed.propertyPath);

            if (prop == null)
            {
                inspectorRoot.Add(new Label($"Can't find the path: {exposed.displayName}"));
                continue;
            }

            inspectorRoot.Add(new PropertyField(prop, exposed.displayName));
        }

        Button unpackButton = new(() =>
        {
            stateSO.isPacked = false;
            EditorUtility.SetDirty(stateSO);
            BuildInspector(stateSO);
        })
        {
            text = "Unpack"
        };
        unpackButton.style.marginTop = 15;
        inspectorRoot.Add(unpackButton);

        if (stateSO.isTemplate)
            inspectorRoot.Add(new HelpBox("This is template state", HelpBoxMessageType.Info));

        inspectorRoot.Bind(serialized);
    }
}
