// using Photon.Pun;
// using UnityEngine;
// using UnityEngine.EventSystems;
//
// public class BoardInput : MonoBehaviour, IPointerClickHandler
// {
//     [SerializeField] private GameManager gm;
//     [SerializeField] private RectTransform boardRect;
//     [SerializeField] private int columns = 7;
//
//     private Canvas canvas;
//     private Camera uiCam;
//
//     private void Awake()
//     {
//         if (boardRect == null) boardRect = transform as RectTransform;
//
//         canvas = GetComponentInParent<Canvas>();
//         uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
//             ? canvas.worldCamera
//             : null;
//     }
//
//     private void Start()
//     {
//         // Always grab the live GameManager (prevents stale inspector ref)
//         gm = FindObjectOfType<GameManager>();
//
//         if (gm == null)
//             Debug.LogError("[INPUT] GameManager not found in scene!");
//
//         // Make sure this UI element can receive clicks
//         var graphic = GetComponent<UnityEngine.UI.Graphic>();
//         if (graphic != null) graphic.raycastTarget = true;
//
//         Debug.Log("[INPUT] BoardInput ready. InRoom=" + PhotonNetwork.InRoom);
//     }
//
//     public void OnPointerClick(PointerEventData eventData)
//     {
//         Debug.Log("[INPUT] Click received on board. pos=" + eventData.position);
//
//         if (gm == null)
//         {
//             gm = FindObjectOfType<GameManager>();
//             if (gm == null) { Debug.LogError("[INPUT] GM still null"); return; }
//         }
//
//         if (boardRect == null) return;
//
//         if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(boardRect, eventData.position, uiCam, out var local))
//         {
//             Debug.LogWarning("[INPUT] ScreenPointToLocalPoint failed.");
//             return;
//         }
//
//         float width = boardRect.rect.width;
//         float normalizedX = (local.x + width * 0.5f) / width;
//
//         int col = Mathf.FloorToInt(normalizedX * columns);
//         col = Mathf.Clamp(col, 0, columns - 1);
//
//         Debug.Log("[INPUT] Column=" + col + " -> calling GM.MakeMove()");
//         gm.MakeMove(col);
//     }
// }
