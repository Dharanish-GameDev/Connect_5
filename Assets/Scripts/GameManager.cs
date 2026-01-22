using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class GameManager : MonoBehaviourPunCallbacks
{
    public static GameManager instance;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
    }

    // ✅ CONNECT 5
    private const int WIN_COUNT = 5;

    [Header("Prefabs (Resources folder names)")]
    public string RedPiecePrefabName = "RedPiecePrefab";
    public string BlackPiecePrefabName = "BlackPiecePrefab";

    [Header("Position References (UI Grid)")]
    [SerializeField] private RectTransform[] rowRects;     // 6 rows
    [SerializeField] private RectTransform[] columnRects;  // 7 cols

    [Header("Canvas Reference")]
    [SerializeField] private RectTransform piecesParent;
    public RectTransform PiecesParent => piecesParent;

    [Header("UI")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI winnerText;

    [Header("Win Line (UI Image)")]
    [SerializeField] private RectTransform winLineImage;
    [SerializeField] private float winLineThickness = 14f;
    [SerializeField] private Color winLineColor = new Color(1f, 1f, 1f, 0.9f);
    private Image winLineImgComponent;

    [Header("Column Highlight")]
    [SerializeField] private Image[] columnHighlights; // optional in inspector (size 7)
    [SerializeField] private Color columnHighlightColor = new Color(1f, 1f, 1f, 0.18f);

    private RectTransform boardSpaceRoot; // parent space of row/col anchors
    private bool isGameOver;
    private PlayerAlliance currentTurn = PlayerAlliance.RED;
    private bool localClickLock;

    public Board GameBoard { get; private set; }

    private const string PROP_TURN = "TURN";
    private const string PROP_BOARD = "BOARD";
    private const string PROP_READY = "READY";

    private void Start()
    {
        ResolveUIRefs();

        GameBoard = new Board();
        isGameOver = false;
        localClickLock = false;

        boardSpaceRoot = (rowRects != null && rowRects.Length > 0) ? rowRects[0].parent as RectTransform : null;

        EnsureWinLineImageExists();
        HideWinLine();

        EnsureColumnHighlightsExist();
        HideAllColumnHighlights();

        if (winnerText != null)
        {
            winnerText.gameObject.SetActive(true);
            winnerText.text = "";
        }

        if (PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
            {
                EnsureRoomPropertiesExist();
                MasterUpdateReadyAndInitTurn();
            }

            ReadTurnFromRoom();
            LoadBoardFromProperty();
        }

        UpdateStatus();
        UpdateUndoButtonState(false);
    }

    private void ResolveUIRefs()
    {
        if (winnerText == null)
        {
            var go = GameObject.Find("WinnerText");
            if (go != null)
            {
                winnerText = go.GetComponent<TextMeshProUGUI>();
                if (winnerText == null) winnerText = go.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }
    }

    // ================================================================
    // INPUT
    // ================================================================
    public void MakeMove(int column)
    {
        if (localClickLock) return;

        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            SetStatus("Connecting...");
            return;
        }

        if (!IsRoomReady())
        {
            SetStatus("Waiting for opponent...");
            return;
        }

        ReadTurnFromRoom();

        if (!CanLocalPlayerInput())
        {
            SetStatus("Not your turn");
            return;
        }

        if (column < 0 || column >= BoardUtils.NUM_COLS) return;

        if (photonView == null || photonView.ViewID == 0)
        {
            Debug.LogError("[GM] PhotonView missing or ViewID=0. Add PhotonView + assign Scene View ID!");
            return;
        }

        localClickLock = true;
        photonView.RPC(nameof(RPC_RequestMove), RpcTarget.MasterClient, column, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    private bool CanLocalPlayerInput()
    {
        if (isGameOver) return false;
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        if (PhotonNetwork.CurrentRoom == null) return false;
        if (!IsRoomReady()) return false;

        if (PhotonNetwork.IsMasterClient && currentTurn == PlayerAlliance.RED) return true;
        if (!PhotonNetwork.IsMasterClient && currentTurn == PlayerAlliance.BLACK) return true;

        return false;
    }

    // ================================================================
    // MASTER AUTHORITATIVE MOVE
    // ================================================================
    [PunRPC]
    private void RPC_RequestMove(int column, int senderActor, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        MasterUpdateReadyAndInitTurn();

        if (!IsRoomReady() || isGameOver || column < 0 || column >= BoardUtils.NUM_COLS)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        if (!CanActorPlayNow(senderActor))
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        int dropRow = FindDropRow(column);
        if (dropRow < 0)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        try
        {
            GameBoard.SetPiece(column, currentTurn);

            // Turn ON highlight for everyone while it drops
            photonView.RPC(nameof(RPC_SetColumnHighlight), RpcTarget.All, column, true);

            SpawnPiece_MasterOnly(column, dropRow, currentTurn);
            SetBoardProperty();

            // ✅ CONNECT-5 WIN CHECK
            Tile placedTile = (currentTurn == PlayerAlliance.RED) ? Tile.RED : Tile.BLACK;
            if (TryGetWinLineFromLastMove(dropRow, column, placedTile, out Vector2 aAnch, out Vector2 bAnch))
            {
                isGameOver = true;

                photonView.RPC(nameof(RPC_GameOverWithUILine), RpcTarget.AllBuffered,
                    (byte)currentTurn,
                    aAnch.x, aAnch.y,
                    bAnch.x, bAnch.y);

                photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
                return;
            }

            currentTurn = (currentTurn == PlayerAlliance.RED) ? PlayerAlliance.BLACK : PlayerAlliance.RED;
            SetTurnProperty(currentTurn);
            photonView.RPC(nameof(RPC_SyncTurn), RpcTarget.AllBuffered, (byte)currentTurn);

            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
        }
        catch
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
        }
    }

    private bool CanActorPlayNow(int actor)
    {
        int masterActor = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;

        if (currentTurn == PlayerAlliance.RED) return actor == masterActor;
        if (currentTurn == PlayerAlliance.BLACK) return actor != masterActor;
        return false;
    }

    private int FindDropRow(int column)
    {
        for (int r = BoardUtils.NUM_ROWS - 1; r >= 0; r--)
        {
            if (GameBoard.Table[r, column] == Tile.EMPTY)
                return r;
        }
        return -1;
    }

    // ================================================================
    // RPCs
    // ================================================================
    [PunRPC]
    private void RPC_SyncTurn(byte turnB)
    {
        currentTurn = (turnB == 0) ? PlayerAlliance.RED : PlayerAlliance.BLACK;
        UpdateStatus();
        localClickLock = false;
    }

    [PunRPC]
    private void RPC_GameOverWithUILine(byte winnerB, float ax, float ay, float bx, float by)
    {
        ResolveUIRefs();
        EnsureWinLineImageExists();

        var winnerAlliance = (winnerB == 0) ? PlayerAlliance.RED : PlayerAlliance.BLACK;
        isGameOver = true;

        HideAllColumnHighlights();

        if (winnerText != null)
        {
            winnerText.gameObject.SetActive(true);
            winnerText.text = (winnerAlliance == PlayerAlliance.RED) ? "RED WON" : "YELLOW WON";
            winnerText.ForceMeshUpdate(true, true);
            winnerText.transform.SetAsLastSibling();
        }

        SetStatus("Game Over");

        Vector2 a = new Vector2(ax, ay);
        Vector2 b = new Vector2(bx, by);
        ShowWinLineBetweenAnchors(a, b);

        localClickLock = false;
    }

    [PunRPC]
    private void RPC_UnlockInput(int senderActor)
    {
        if (PhotonNetwork.LocalPlayer != null &&
            PhotonNetwork.LocalPlayer.ActorNumber == senderActor)
        {
            localClickLock = false;
        }
    }

    [PunRPC]
    private void RPC_SetColumnHighlight(int column, bool on)
    {
        if (columnHighlights == null || columnHighlights.Length != BoardUtils.NUM_COLS) return;

        if (!on)
        {
            if (column >= 0 && column < columnHighlights.Length && columnHighlights[column] != null)
                columnHighlights[column].gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < columnHighlights.Length; i++)
            if (columnHighlights[i] != null) columnHighlights[i].gameObject.SetActive(false);

        if (column >= 0 && column < columnHighlights.Length && columnHighlights[column] != null)
            columnHighlights[column].gameObject.SetActive(true);
    }

    public void ClearColumnHighlightLocal(int column)
    {
        if (isGameOver) return;
        if (columnHighlights == null || columnHighlights.Length != BoardUtils.NUM_COLS) return;
        if (column < 0 || column >= columnHighlights.Length) return;
        if (columnHighlights[column] == null) return;

        columnHighlights[column].gameObject.SetActive(false);
    }

    // ================================================================
    // PIECE SPAWN (MASTER ONLY)
    // ================================================================
    private void SpawnPiece_MasterOnly(int column, int row, PlayerAlliance alliance)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        Vector2 anchoredPosition = GetAnchoredPosForCell(row, column);
        string prefabName = (alliance == PlayerAlliance.BLACK) ? BlackPiecePrefabName : RedPiecePrefabName;

        PhotonView parentPhotonView = piecesParent.GetComponent<PhotonView>();
        int parentViewID = parentPhotonView != null ? parentPhotonView.ViewID : 0;

        PhotonNetwork.InstantiateRoomObject(
            prefabName,
            Vector3.zero,
            Quaternion.identity,
            0,
            new object[] { anchoredPosition.x, anchoredPosition.y, parentViewID, column }
        );
    }

    private Vector2 GetAnchoredPosForCell(int boardRow, int boardCol)
    {
        int uiRowIndex = BoardUtils.NUM_ROWS - boardRow - 1;
        RectTransform col = columnRects[boardCol];
        RectTransform row = rowRects[uiRowIndex];
        return new Vector2(col.anchoredPosition.x, row.anchoredPosition.y);
    }

    // ================================================================
    // WIN CHECK (CONNECT 5)
    // ================================================================
    private bool TryGetWinLineFromLastMove(int r, int c, Tile t, out Vector2 aAnch, out Vector2 bAnch)
    {
        aAnch = Vector2.zero;
        bAnch = Vector2.zero;

        int[,] dirs = new int[,] { { 0, 1 }, { 1, 0 }, { 1, 1 }, { 1, -1 } };

        for (int i = 0; i < 4; i++)
        {
            int dr = dirs[i, 0];
            int dc = dirs[i, 1];

            int minR = r, minC = c;
            int maxR = r, maxC = c;

            int rr = r - dr, cc = c - dc;
            while (IsInside(rr, cc) && GameBoard.Table[rr, cc] == t)
            {
                minR = rr; minC = cc;
                rr -= dr; cc -= dc;
            }

            rr = r + dr; cc = c + dc;
            while (IsInside(rr, cc) && GameBoard.Table[rr, cc] == t)
            {
                maxR = rr; maxC = cc;
                rr += dr; cc += dc;
            }

            int len = Mathf.Max(Mathf.Abs(maxR - minR), Mathf.Abs(maxC - minC)) + 1;

            if (len >= WIN_COUNT)
            {
                aAnch = GetAnchoredPosForCell(minR, minC);
                bAnch = GetAnchoredPosForCell(maxR, maxC);
                return true;
            }
        }

        return false;
    }

    private bool IsInside(int r, int c)
    {
        return r >= 0 && r < BoardUtils.NUM_ROWS && c >= 0 && c < BoardUtils.NUM_COLS;
    }

    // ================================================================
    // WIN LINE UI IMAGE
    // ================================================================
    private void EnsureWinLineImageExists()
    {
        if (winLineImage != null && winLineImgComponent != null) return;

        if (winLineImage == null)
        {
            GameObject go = new GameObject("WinLineImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(piecesParent, false);

            winLineImage = go.GetComponent<RectTransform>();
            winLineImgComponent = go.GetComponent<Image>();
            winLineImgComponent.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        }
        else
        {
            winLineImgComponent = winLineImage.GetComponent<Image>();
            if (winLineImgComponent == null) winLineImgComponent = winLineImage.gameObject.AddComponent<Image>();
        }

        winLineImgComponent.color = winLineColor;
        winLineImgComponent.raycastTarget = false;

        winLineImage.anchorMin = new Vector2(0.5f, 0.5f);
        winLineImage.anchorMax = new Vector2(0.5f, 0.5f);
        winLineImage.pivot = new Vector2(0.5f, 0.5f);
        winLineImage.SetAsLastSibling();
    }

    private void HideWinLine()
    {
        if (winLineImage != null)
            winLineImage.gameObject.SetActive(false);
    }

    private void ShowWinLineBetweenAnchors(Vector2 a, Vector2 b)
    {
        if (winLineImage == null) return;

        winLineImage.gameObject.SetActive(true);
        winLineImage.SetAsLastSibling();

        Vector2 mid = (a + b) * 0.5f;
        float length = Vector2.Distance(a, b);
        float angle = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;

        winLineImage.anchoredPosition = mid;
        winLineImage.sizeDelta = new Vector2(length, winLineThickness);
        winLineImage.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    // ================================================================
    // COLUMN HIGHLIGHT SETUP
    // ================================================================
    private void EnsureColumnHighlightsExist()
    {
        if (boardSpaceRoot == null) return;

        if (columnHighlights != null && columnHighlights.Length == BoardUtils.NUM_COLS)
            return;

        columnHighlights = new Image[BoardUtils.NUM_COLS];

        for (int c = 0; c < BoardUtils.NUM_COLS; c++)
        {
            GameObject go = new GameObject($"ColHighlight_{c}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(boardSpaceRoot, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            Image img = go.GetComponent<Image>();

            img.color = columnHighlightColor;
            img.raycastTarget = false;

            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            float width = (columnRects != null && columnRects.Length > c) ? Mathf.Max(60f, columnRects[c].rect.width) : 80f;
            rt.sizeDelta = new Vector2(width, 0f);

            float x = (columnRects != null && columnRects.Length > c) ? columnRects[c].anchoredPosition.x : 0f;
            rt.anchoredPosition = new Vector2(x, 0f);

            go.SetActive(false);
            columnHighlights[c] = img;
        }
    }

    private void HideAllColumnHighlights()
    {
        if (columnHighlights == null) return;
        for (int i = 0; i < columnHighlights.Length; i++)
            if (columnHighlights[i] != null)
                columnHighlights[i].gameObject.SetActive(false);
    }

    // ================================================================
    // ROOM PROPERTIES / STATUS (unchanged)
    // ================================================================
    private void EnsureRoomPropertiesExist()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PROP_READY))
            SetReadyProperty(false);

        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PROP_TURN))
            SetTurnProperty(PlayerAlliance.RED);

        if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PROP_BOARD))
            SetBoardProperty();
    }

    private void MasterUpdateReadyAndInitTurn()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (PhotonNetwork.CurrentRoom == null) return;

        bool ready = PhotonNetwork.CurrentRoom.PlayerCount >= 2;
        SetReadyProperty(ready);

        if (ready && !PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PROP_TURN))
            SetTurnProperty(PlayerAlliance.RED);
    }

    private bool IsRoomReady()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return false;

        if (PhotonNetwork.CurrentRoom.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_READY, out object val))
        {
            return (int)val == 1;
        }
        return false;
    }

    private void SetReadyProperty(bool ready)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        var props = new Hashtable();
        props[PROP_READY] = ready ? 1 : 0;
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void SetTurnProperty(PlayerAlliance turn)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        var props = new Hashtable();
        props[PROP_TURN] = (turn == PlayerAlliance.RED) ? 0 : 1;
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void ReadTurnFromRoom()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        if (PhotonNetwork.CurrentRoom.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_TURN, out object val))
        {
            int t = (int)val;
            currentTurn = (t == 0) ? PlayerAlliance.RED : PlayerAlliance.BLACK;
        }
        else
        {
            currentTurn = PlayerAlliance.RED;
        }
    }

    private void SetBoardProperty()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(BoardUtils.NUM_ROWS * BoardUtils.NUM_COLS);

        for (int r = 0; r < BoardUtils.NUM_ROWS; r++)
        {
            for (int c = 0; c < BoardUtils.NUM_COLS; c++)
            {
                Tile tile = GameBoard.Table[r, c];
                if (tile == Tile.EMPTY) sb.Append('0');
                else if (tile == Tile.RED) sb.Append('1');
                else sb.Append('2');
            }
        }

        var props = new Hashtable();
        props[PROP_BOARD] = sb.ToString();
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    private void LoadBoardFromProperty()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null) return;

        if (!PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_BOARD, out object val))
            return;

        string s = val as string;
        if (string.IsNullOrEmpty(s) || s.Length != BoardUtils.NUM_ROWS * BoardUtils.NUM_COLS)
            return;

        GameBoard = new Board();

        int idx = 0;
        for (int r = 0; r < BoardUtils.NUM_ROWS; r++)
        {
            for (int c = 0; c < BoardUtils.NUM_COLS; c++)
            {
                char ch = s[idx++];
                if (ch == '1') GameBoard.Table[r, c] = Tile.RED;
                else if (ch == '2') GameBoard.Table[r, c] = Tile.BLACK;
                else GameBoard.Table[r, c] = Tile.EMPTY;
            }
        }
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged == null) return;

        if (propertiesThatChanged.ContainsKey(PROP_READY) || propertiesThatChanged.ContainsKey(PROP_TURN))
        {
            ReadTurnFromRoom();
            UpdateStatus();
        }
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        else if (!string.IsNullOrEmpty(msg)) Debug.Log(msg);
    }

    private void UpdateStatus()
    {
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
        {
            SetStatus("Connecting...");
            return;
        }

        if (!IsRoomReady())
        {
            SetStatus("Waiting for opponent...");
            return;
        }

        if (isGameOver)
        {
            SetStatus("Game Over");
            return;
        }

        ReadTurnFromRoom();

        if (!CanLocalPlayerInput())
        {
            SetStatus("Opponent's turn");
            return;
        }

        SetStatus("Your turn");
    }

    private void UpdateUndoButtonState(bool enabled)
    {
        GameObject btn = GameObject.Find("UndoButton");
        if (btn != null)
        {
            Button undoButton = btn.GetComponent<Button>();
            if (undoButton != null) undoButton.interactable = enabled;
        }
    }

    public override void OnJoinedRoom()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            EnsureRoomPropertiesExist();
            MasterUpdateReadyAndInitTurn();
        }

        ReadTurnFromRoom();
        LoadBoardFromProperty();
        UpdateStatus();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
            MasterUpdateReadyAndInitTurn();

        UpdateStatus();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
            MasterUpdateReadyAndInitTurn();

        SetStatus("Opponent left");
        localClickLock = false;
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            EnsureRoomPropertiesExist();
            MasterUpdateReadyAndInitTurn();
        }

        UpdateStatus();
    }
}
