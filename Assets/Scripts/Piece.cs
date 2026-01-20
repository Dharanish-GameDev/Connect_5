using Photon.Pun;
using UnityEngine;

public class Connect4Piece : MonoBehaviourPun
{
    [Header("Drop Settings")]
    public float speed = 12f;

    private RectTransform rectTransform;
    private float xDest;
    private float yDest;
    private bool hasTarget;
    private Vector2 targetPosition;
    private Vector2 startPosition;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        
        // Parent to pieces container
        GameManager gm = GameManager.instance;
        if (gm != null && gm.PiecesParent != null)
        {
            transform.SetParent(gm.PiecesParent, false);
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }
        
        // Get instantiation data
        object[] data = photonView.InstantiationData;
        if (data != null && data.Length >= 2)
        {
            xDest = (float)data[0];
            yDest = (float)data[1];
            targetPosition = new Vector2(xDest, yDest);
            
            // Set start position (above the board)
            startPosition = new Vector2(xDest, GetDropStartHeight());
            Debug.Log("Drop Height: " + GetDropStartHeight());
            rectTransform.anchoredPosition = startPosition;
            
            hasTarget = true;
            transform.SetAsLastSibling();
        }
        else
        {
            hasTarget = false;
            Debug.LogWarning("[Connect4Piece] Missing InstantiationData.");
        }
    }

    private float GetDropStartHeight()
    {
        // Adjust based on your canvas and board layout
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            // Start from top of canvas
            return canvas.pixelRect.height / 2 + 100f;
        }
        return 10f; // Default fallback
    }

    private void Update()
    {
        if (!hasTarget) return;

        Vector2 currentPos = rectTransform.anchoredPosition;
        rectTransform.anchoredPosition = Vector2.MoveTowards(
            currentPos, 
            targetPosition, 
            speed * Time.deltaTime
        );

        // Check if reached destination
        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < 0.1f)
        {
            rectTransform.anchoredPosition = targetPosition;
            hasTarget = false;
            OnDropComplete();
        }
    }

    private void OnDropComplete()
    {
        // Optional: Trigger any completion events
        // You could broadcast an RPC here if needed
        photonView.RPC("RPC_OnDropComplete", RpcTarget.All);
    }

    [PunRPC]
    private void RPC_OnDropComplete()
    {
        // All clients execute this when piece lands
        // Could play sound, trigger effects, etc.
    }
}