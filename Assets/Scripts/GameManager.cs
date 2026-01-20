using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using ExitGames.Client.Photon;

public class GameManager : MonoBehaviourPunCallbacks
{
    // -------------------- SINGLETON (prevents duplicate managers) --------------------
    public static GameManager instance;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    [Header("Prefabs (Resources folder names)")]
    public string RedPiecePrefabName = "RedPiecePrefab";
    public string BlackPiecePrefabName = "BlackPiecePrefab";
    
    [Header("Position References")]
    [SerializeField] private RectTransform[] rowRects; // 6 RectTransforms for rows
    [SerializeField] private RectTransform[] columnRects; // 7 RectTransforms for columns
    
    [Header("Canvas Reference")]
    [SerializeField] private RectTransform piecesParent; // The canvas area for pieces
    
    public RectTransform PiecesParent => piecesParent;

    [Header("UI")]
    public GameObject WinnerText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Win Line")]
    private const float lineSize = 0.3f;
    public Material LineMaterial;

    [Header("Board World Positions")]
    private float[] yCoordinates = new float[] { -3.319f, -1.9f, -0.486f, 0.933f, 2.348f, 3.767f };
    private float[] xCoordinates = new float[] { -4.25f, -2.7914f, -1.3306f, 0.128f, 1.5826f, 3.0434f, 4.502f };
    private float pieceStartingY = 8.0f;

    private Undo undoStack;
    private bool IsGameOver;

    // Master authoritative turn
    private PlayerAlliance currentTurn = PlayerAlliance.RED;

    // Local click lock
    private bool localClickLock = false;

    public Board GameBoard { get; private set; }

    // -------------------- ROOM PROPERTIES --------------------
    private const string PROP_TURN = "TURN";    // 0=RED, 1=BLACK
    private const string PROP_BOARD = "BOARD";  // 42 chars: 0 empty, 1 red, 2 black
    private const string PROP_READY = "READY";  // 0 waiting, 1 ready

    private void Start()
    {
        GameBoard = new Board();
        undoStack = new Undo();
        IsGameOver = false;
        localClickLock = false;

        if (WinnerText != null)
            WinnerText.GetComponent<TextMeshProUGUI>().text = "";

        // RPC requires PhotonView with valid ViewID
        if (photonView == null || photonView.ViewID == 0)
        {
            Debug.LogError("[GM] PhotonView missing or ViewID=0. Add PhotonView + assign Scene View ID!");
        }

        // If already in room, sync state
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

        UpdateUIElements();
        UpdateStatus();
        UpdateUndoButtonState(false);

        Debug.Log("[GM] Started. Master=" + PhotonNetwork.IsMasterClient);
    }

    // ================================================================
    // INPUT
    // ================================================================
    public bool CanLocalPlayerInput()
    {
        if (IsGameOver) return false;
        if (!PhotonNetwork.IsConnected || !PhotonNetwork.InRoom) return false;
        if (PhotonNetwork.CurrentRoom == null) return false;

        if (!IsRoomReady()) return false;

        // Master plays RED, non-master plays BLACK
        if (PhotonNetwork.IsMasterClient && currentTurn == PlayerAlliance.RED) return true;
        if (!PhotonNetwork.IsMasterClient && currentTurn == PlayerAlliance.BLACK) return true;

        return false;
    }

    // Called by BoardInput
    public void MakeMove(int column)
    {
        Debug.Log($"[GM] CLICK col={column} ready={IsRoomReady()} master={PhotonNetwork.IsMasterClient} turn={currentTurn}");

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

        // Refresh turn before gating
        ReadTurnFromRoom();

        if (!CanLocalPlayerInput())
        {
            SetStatus("Not your turn");
            return;
        }

        if (column < 0 || column >= BoardUtils.NUM_COLS) return;

        if (photonView == null || photonView.ViewID == 0)
        {
            Debug.LogError("[GM] PhotonView missing or ViewID=0.");
            return;
        }

        localClickLock = true;

        // Request to master only
        photonView.RPC(nameof(RPC_RequestMove), RpcTarget.MasterClient, column, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    // ================================================================
    // MASTER AUTHORITATIVE MOVE
    // ================================================================
    [PunRPC]
    private void RPC_RequestMove(int column, int senderActor, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        Debug.Log("[GM] RequestMove for Column : " + column);
        // Ensure ready is correct on master
        MasterUpdateReadyAndInitTurn();
        if (!IsRoomReady())
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        if (IsGameOver)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        if (column < 0 || column >= BoardUtils.NUM_COLS)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        // Enforce turn: master is RED, other is BLACK
        if (!CanActorPlayNow(senderActor))
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        // Compute drop row BEFORE placing
        int dropRow = FindDropRow(column);
        if (dropRow < 0)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
            return;
        }

        try
        {
            // Apply on master only
            GameBoard.SetPiece(column, currentTurn);

            // Spawn network piece (master only)
            SpawnPiece_MasterOnly(column, dropRow, currentTurn);

            // Snapshot board for late join safety
            SetBoardProperty();

            // Win check (master only)
            if (Connect4Utils.Finished(GameBoard)) 
            {
                IsGameOver = true;
                photonView.RPC(nameof(RPC_GameOver), RpcTarget.AllBuffered, (byte)currentTurn);
                photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
                return;
            }

            // Switch turn (master only) + broadcast
            currentTurn = (currentTurn == PlayerAlliance.RED) ? PlayerAlliance.BLACK : PlayerAlliance.RED;
            SetTurnProperty(currentTurn);
            photonView.RPC(nameof(RPC_SyncTurn), RpcTarget.AllBuffered, (byte)currentTurn);

            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
        }
        catch (ColumnOccupiedException)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
        }
        catch (InvalidColumnException)
        {
            photonView.RPC(nameof(RPC_UnlockInput), RpcTarget.All, senderActor);
        }
    }

    [PunRPC]
    private void RPC_SyncTurn(byte turnB)
    {
        currentTurn = (turnB == 0) ? PlayerAlliance.RED : PlayerAlliance.BLACK;
        UpdateUIElements();
        UpdateStatus();
        localClickLock = false;
    }

    [PunRPC]
    private void RPC_GameOver(byte winnerB)
    {
        var winner = (winnerB == 0) ? PlayerAlliance.RED : PlayerAlliance.BLACK;
        IsGameOver = true;
        HandleEndGame(winner);
        localClickLock = false;
    }

    [PunRPC]
    private void RPC_UnlockInput(int senderActor)
    {
        if (PhotonNetwork.LocalPlayer != null && PhotonNetwork.LocalPlayer.ActorNumber == senderActor)
            localClickLock = false;
    }

    private bool CanActorPlayNow(int actor)
    {
        int masterActor = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;

        if (currentTurn == PlayerAlliance.RED) return actor == masterActor;
        if (currentTurn == PlayerAlliance.BLACK) return actor != masterActor;
        return false;
    }

    // Board drops from bottom (NUM_ROWS-1) upward
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
    // PIECE SPAWN (MASTER ONLY) + InstantiationData for xDest/yDest
    // ================================================================
    private void SpawnPiece_MasterOnly(int column, int row, PlayerAlliance alliance)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // Get the RectTransforms for column and row
        RectTransform columnRect = columnRects[column];
        RectTransform rowRect = rowRects[BoardUtils.NUM_ROWS - row - 1];
        
        // Calculate position using anchored positions
        Vector2 anchoredPosition = new Vector2(
            columnRect.anchoredPosition.x,
            rowRect.anchoredPosition.y
        );

        string prefabName = (alliance == PlayerAlliance.BLACK) ? BlackPiecePrefabName : RedPiecePrefabName;

        // Get the parent's PhotonView ID
        PhotonView parentPhotonView = piecesParent.GetComponent<PhotonView>();
        int parentViewID = parentPhotonView != null ? parentPhotonView.ViewID : 0;

        PhotonNetwork.InstantiateRoomObject(
            prefabName,
            Vector3.zero,
            Quaternion.identity,
            0,
            new object[] { 
                anchoredPosition.x, 
                anchoredPosition.y,
                parentViewID // Pass parent's view ID
            }
        );
    }


    // ================================================================
    // ROOM PROPERTIES
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

        // When match becomes ready, ensure turn starts at RED (master)
        if (ready)
        {
            // If TURN is missing (or corrupted), reset to RED
            if (!PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(PROP_TURN))
                SetTurnProperty(PlayerAlliance.RED);
        }

        Debug.Log("[GM] READY=" + (ready ? 1 : 0) + " players=" + PhotonNetwork.CurrentRoom.PlayerCount);
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
                Tile t = GameBoard.Table[r, c];
                if (t == Tile.EMPTY) sb.Append('0');
                else if (t == Tile.RED) sb.Append('1');
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
            UpdateUIElements();
            UpdateStatus();
        }
    }

    // ================================================================
    // UI
    // ================================================================
    private void UpdateUIElements()
    {
        var textMesh = GameObject.FindGameObjectWithTag("PlayerTextMesh");
        if (textMesh != null)
        {
            var playerText = textMesh.GetComponent<TextMeshProUGUI>();
            if (playerText != null)
                playerText.text = (currentTurn == PlayerAlliance.RED) ? "RED TURN" : "BLACK TURN";
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

        ReadTurnFromRoom();

        if (!CanLocalPlayerInput())
        {
            SetStatus("Opponent's turn");
            return;
        }

        SetStatus("Your turn");
    }

    // ================================================================
    // END GAME
    // ================================================================
    private void HandleEndGame(PlayerAlliance winner)
    {
        UpdateUndoButtonState(false);

        if (WinnerText != null)
            WinnerText.GetComponent<TextMeshProUGUI>().text = (winner == PlayerAlliance.RED) ? "RED WON" : "BLACK WON";

        SetStatus("Game Over");
        
        Debug.Log("Game Over");
    }

    // ================================================================
    // UNDO DISABLED ONLINE
    // ================================================================
    private void UpdateUndoButtonState(bool enabled)
    {
        GameObject btn = GameObject.Find("UndoButton");
        if (btn != null)
        {
            Button undoButton = btn.GetComponent<Button>();
            if (undoButton != null)
                undoButton.interactable = enabled;
        }
    }

    // ================================================================
    // ROOM EVENTS
    // ================================================================
    public override void OnJoinedRoom()
    {
        Debug.Log("[GM] OnJoinedRoom players=" + PhotonNetwork.CurrentRoom.PlayerCount);

        if (PhotonNetwork.IsMasterClient)
        {
            EnsureRoomPropertiesExist();
            MasterUpdateReadyAndInitTurn();
        }

        ReadTurnFromRoom();
        LoadBoardFromProperty();

        UpdateUIElements();
        UpdateStatus();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log("[GM] Player entered room. players=" + PhotonNetwork.CurrentRoom.PlayerCount);

        if (PhotonNetwork.IsMasterClient)
            MasterUpdateReadyAndInitTurn();

        UpdateStatus();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log("[GM] Player left room.");

        if (PhotonNetwork.IsMasterClient)
            MasterUpdateReadyAndInitTurn();

        SetStatus("Opponent left");
        localClickLock = false;
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        Debug.Log("[GM] Master switched -> " + newMasterClient);

        // New master should immediately recompute READY
        if (PhotonNetwork.IsMasterClient)
        {
            EnsureRoomPropertiesExist();
            MasterUpdateReadyAndInitTurn();
        }

        UpdateStatus();
    }
}
