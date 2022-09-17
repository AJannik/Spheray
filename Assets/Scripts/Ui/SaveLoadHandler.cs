using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Ui
{
    public class SaveLoadHandler : MonoBehaviour
    {
        [SerializeField] private UiEventChannel uiEventChannel;
        [SerializeField] private TMP_InputField saveNameTxt;
        [SerializeField] private TMP_InputField loadNameTxt;

        public void OnSaveBtn()
        {
            string fileName = saveNameTxt.text;
            fileName = fileName.Trim();

            if (fileName.Equals(""))
            {
                Debug.Log("File name is empty!");
                return;
            }

            string[] fileNames = Directory.EnumerateFiles(Application.dataPath + "/data", "*.json").Select(Path.GetFileName).ToArray();
            if (fileNames.Contains($"{fileName}.json"))
            {
                Debug.Log($"There is already a file named {fileName}!");
                return;
            }

            uiEventChannel.RaiseSaveSDF(fileName);
        }

        public void OnLoadBtn()
        {
            string fileName = loadNameTxt.text;
            fileName = fileName.Trim();
            string[] files = Directory.EnumerateFiles(Application.dataPath + "/data", "*.json").Select(Path.GetFileName).ToArray();
            if (!files.Contains($"{fileName}.json"))
            {
                Debug.Log($"File {fileName} not found!");
                return;
            }
            
            try
            {
                string json = File.ReadAllText(Application.dataPath + $"/data/{fileName}.json");
                DataHolder data = (DataHolder) JsonUtility.FromJson(json, typeof(DataHolder));
                uiEventChannel.RaiseLoadSDF(data);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }
    }
}
