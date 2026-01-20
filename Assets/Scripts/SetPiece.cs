using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;

public class UndoButton : MonoBehaviour
{
    private Button btn;

    private void Awake()
    {
        btn = GetComponent<Button>();
    }

    private void Start()
    {
        // // ✅ Disable undo for multiplayer to prevent desync
        // if (btn != null)
        //     btn.interactable = false;
    }

    // Hook this to Button OnClick()
    public void OnUndoPressed()
    {
        Debug.Log("Undo disabled in online multiplayer.");
    }
}
