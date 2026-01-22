using Photon.Pun;
using UnityEngine;

public class Connect4Piece : MonoBehaviourPun
{
    [Header("Drop Settings")]
    public float speed = 12f;

    private RectTransform rectTransform;

    private Vector2 targetPosition;
    private Vector2 startPosition;

    private bool hasTarget;

    // ✅ NEW: column index (passed by GameManager)
    private int columnIndex = -1;

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

        // Read instantiation data
        object[] data = photonView.InstantiationData;

        // Expected:
        // [0] = x
        // [1] = y
        // [2] = parentViewID
        // [3] = column index   ✅
        if (data != null && data.Length >= 4)
        {
            float xDest = (float)data[0];
            float yDest = (float)data[1];
            columnIndex = (int)data[3];

            targetPosition = new Vector2(xDest, yDest);

            // Start above board
            startPosition = new Vector2(xDest, GetDropStartHeight());
            rectTransform.anchoredPosition = startPosition;

            hasTarget = true;
            transform.SetAsLastSibling();
        }
        else
        {
            hasTarget = false;
            Debug.LogWarning("[Connect4Piece] Missing or invalid InstantiationData.");
        }
    }

    private float GetDropStartHeight()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            return canvas.pixelRect.height / 2f + 120f;
        }
        return 600f; // fallback
    }

    private void Update()
    {
        if (!hasTarget) return;

        rectTransform.anchoredPosition = Vector2.MoveTowards(
            rectTransform.anchoredPosition,
            targetPosition,
            speed * Time.deltaTime
        );

        if (Vector2.Distance(rectTransform.anchoredPosition, targetPosition) < 0.1f)
        {
            rectTransform.anchoredPosition = targetPosition;
            hasTarget = false;
            OnDropComplete();
        }
    }

    private void OnDropComplete()
    {
        // ✅ Clear column highlight locally (no RPC needed)
        if (columnIndex >= 0 && GameManager.instance != null)
        {
            GameManager.instance.ClearColumnHighlightLocal(columnIndex);
        }

        // Optional sync hook (sound / FX)
        photonView.RPC(nameof(RPC_OnDropComplete), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_OnDropComplete()
    {
        // Optional shared effects (sound, particles, etc.)
    }
}
