using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "UIEventChannel", menuName = "EventChannel/UI")]
public class UiEventChannel : ScriptableObject
{
    public UnityAction<string> SaveSDF;
    public UnityAction<DataHolder> LoadSDF;

    public void RaiseSaveSDF(string filename)
    {
        SaveSDF?.Invoke(filename);
    }
    
    public void RaiseLoadSDF(DataHolder data)
    {
        LoadSDF?.Invoke(data);
    }
}