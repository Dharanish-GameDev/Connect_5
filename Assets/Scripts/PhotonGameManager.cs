using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public enum Piece : byte { Empty = 0, Red = 1, Yellow = 2 }
public enum GameStatus : byte { Playing = 0, RedWon = 1, YellowWon = 2, Draw = 3 }

public class PhotonGameManager : MonoBehaviourPunCallbacks
{
    public const int Columns = 7;
    public const int Rows = 6;

    private Piece[,] board = new Piece[Rows, Columns];

    private Piece currentTurn = Piece.Red;
    private GameStatus status = GameStatus.Playing;

    private int redActor = -1;
    private int yellowActor = -1;

    private double lastMoveTimeMaster = -999;
    private const double MoveCooldown = 0.15;

    private void Start()
    {
        AssignPlayers();

        if (PhotonNetwork.IsMasterClient)
        {
            // Clean start for buffered RPCs for this PhotonView
            PhotonNetwork.RemoveRPCs(photonView);
        }

        RefreshUI();
    }

    private void AssignPlayers()
    {
        var players = PhotonNetwork.PlayerList;

        redActor = (players.Length >= 1) ? players[0].ActorNumber : -1;
        yellowActor = (players.Length >= 2) ? players[1].ActorNumber : -1;
    }

    public void RequestMove(int col)
    {
        if (status != GameStatus.Playing) return;
        if (!IsMyTurn()) return;

        photonView.RPC(nameof(RPC_RequestMove), RpcTarget.MasterClient, col, PhotonNetwork.LocalPlayer.ActorNumber);
    }

    private bool IsMyTurn()
    {
        if (PhotonNetwork.LocalPlayer == null) return false;

        int me = PhotonNetwork.LocalPlayer.ActorNumber;
        if (currentTurn == Piece.Red) return me == redActor;
        if (currentTurn == Piece.Yellow) return me == yellowActor;
        return false;
    }

    [PunRPC]
    private void RPC_RequestMove(int col, int senderActor, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (status != GameStatus.Playing) return;
        if (col < 0 || col >= Columns) return;

        // Ensure seats are valid
        if (redActor == -1 || yellowActor == -1) AssignPlayers();

        if (PhotonNetwork.Time - lastMoveTimeMaster < MoveCooldown) return;
        lastMoveTimeMaster = PhotonNetwork.Time;

        // Turn validation
        if (currentTurn == Piece.Red && senderActor != redActor) return;
        if (currentTurn == Piece.Yellow && senderActor != yellowActor) return;

        int row = FindDropRow(col);
        if (row < 0) return;

        board[row, col] = currentTurn;

        GameStatus newStatus = status;
        if (CheckWin(row, col, currentTurn))
            newStatus = (currentTurn == Piece.Red) ? GameStatus.RedWon : GameStatus.YellowWon;
        else if (IsBoardFull())
            newStatus = GameStatus.Draw;

        Piece nextTurn = currentTurn;
        if (newStatus == GameStatus.Playing)
            nextTurn = (currentTurn == Piece.Red) ? Piece.Yellow : Piece.Red;

        photonView.RPC(nameof(RPC_ApplyMove), RpcTarget.AllBuffered,
            row, col, (byte)currentTurn, (byte)nextTurn, (byte)newStatus);
    }

    [PunRPC]
    private void RPC_ApplyMove(int row, int col, byte pieceB, byte nextTurnB, byte statusB)
    {
        Piece piece = (Piece)pieceB;

        board[row, col] = piece;
        currentTurn = (Piece)nextTurnB;
        status = (GameStatus)statusB;

        RefreshUI();
        RenderPiece(row, col, piece);
    }

    public void RestartGame()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        PhotonNetwork.RemoveRPCs(photonView);
        photonView.RPC(nameof(RPC_Reset), RpcTarget.AllBuffered);
    }

    [PunRPC]
    private void RPC_Reset()
    {
        board = new Piece[Rows, Columns];
        status = GameStatus.Playing;
        currentTurn = Piece.Red;

        AssignPlayers();
        RefreshUI();
        RedrawBoard();
    }

    private int FindDropRow(int col)
    {
        for (int r = Rows - 1; r >= 0; r--)
            if (board[r, col] == Piece.Empty) return r;
        return -1;
    }

    private bool IsBoardFull()
    {
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                if (board[r, c] == Piece.Empty) return false;
        return true;
    }

    private bool CheckWin(int r, int c, Piece p)
    {
        return Count(r, c, 0, 1, p) + Count(r, c, 0, -1, p) - 1 >= 4 ||
               Count(r, c, 1, 0, p) + Count(r, c, -1, 0, p) - 1 >= 4 ||
               Count(r, c, 1, 1, p) + Count(r, c, -1, -1, p) - 1 >= 4 ||
               Count(r, c, 1, -1, p) + Count(r, c, -1, 1, p) - 1 >= 4;
    }

    private int Count(int r, int c, int dr, int dc, Piece p)
    {
        int cnt = 0;
        int rr = r, cc = c;
        while (rr >= 0 && rr < Rows && cc >= 0 && cc < Columns && board[rr, cc] == p)
        {
            cnt++;
            rr += dr;
            cc += dc;
        }
        return cnt;
    }

    // ---- UI/Rendering hooks ----
    private void RefreshUI()
    {
        // Update UI from (currentTurn, status)
    }

    private void RenderPiece(int row, int col, Piece piece)
    {
        // Spawn / update visual at (row,col)
    }

    private void RedrawBoard()
    {
        // Clear visuals and re-render from board[,] for safety
    }

    // ✅ FIX 1: fully-qualified Player type so override always matches PUN2
    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        if (PhotonNetwork.IsMasterClient)
        {
            AssignPlayers();

            // Optional safety: if a new player joins mid-game, force a reset
            // RestartGame();
        }
    }

    // ✅ (recommended) keep seats valid when someone leaves
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        AssignPlayers();

        // If game breaks when a player leaves, safest is reset once a new master exists
        if (PhotonNetwork.IsMasterClient)
        {
            // RestartGame();
        }
    }

    // ✅ FIX 2: fully-qualified Player type
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // New master becomes authority; safest is reset so everyone re-syncs
        if (PhotonNetwork.IsMasterClient)
            RestartGame();
    }
}
