using UnityEngine;
using UnityEngine.SceneManagement;

public class GamePanel : MonoBehaviour
{
    public void OnBackButtonClicked()
    {
        int currentSceneIndex = SceneManager.GetActiveScene().buildIndex;
        SceneManager.LoadScene(currentSceneIndex - 1);
    }

    public void OnUndoButtonClicked()
    {
        Debug.Log("Undo disabled in online multiplayer.");
        // If you want, show UI message here instead of Debug.Log.
    }

    public void PlayAgainPressed()
    {
        Debug.Log("PRESSED AGAIN!");
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
