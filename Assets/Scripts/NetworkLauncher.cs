using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class NetworkLauncher : MonoBehaviourPunCallbacks
{
    [Header("Room")]
    [SerializeField] private string roomName = "CONNECT4_ROOM";
    [SerializeField] private byte maxPlayers = 2;

    [Header("Version (must match on both clients)")]
    [SerializeField] private string gameVersion = "connect4_v1";

    private bool joining;

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = false;
        PhotonNetwork.GameVersion = gameVersion;

        Debug.Log("[NL] Connecting... Version=" + PhotonNetwork.GameVersion);
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("[NL] ConnectedToMaster. Region=" + PhotonNetwork.CloudRegion);

        if (joining) return;
        joining = true;

        // Try join first; if it fails, create
        PhotonNetwork.JoinRoom(roomName);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogWarning($"[NL] JoinRoomFailed code={returnCode} msg={message}. Creating room...");

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            IsOpen = true,
            IsVisible = true,
            CleanupCacheOnLeave = true
        };

        PhotonNetwork.CreateRoom(roomName, options, TypedLobby.Default);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"[NL] CreateRoomFailed code={returnCode} msg={message}. Retrying join...");
        // Someone else may have created it at the same time, retry join
        PhotonNetwork.JoinRoom(roomName);
    }

    public override void OnJoinedRoom()
    {
        joining = false;

        Debug.Log("[NL] JoinedRoom: " + PhotonNetwork.CurrentRoom.Name +
                  " players=" + PhotonNetwork.CurrentRoom.PlayerCount +
                  " master=" + PhotonNetwork.IsMasterClient);

        // Lock room if full
        TryLockOrUnlockRoom();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log("[NL] PlayerEnteredRoom actor=" + newPlayer.ActorNumber +
                  " players=" + PhotonNetwork.CurrentRoom.PlayerCount);

        TryLockOrUnlockRoom();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log("[NL] PlayerLeftRoom actor=" + otherPlayer.ActorNumber +
                  " players=" + PhotonNetwork.CurrentRoom.PlayerCount);

        TryLockOrUnlockRoom();
    }

    private void TryLockOrUnlockRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (PhotonNetwork.CurrentRoom == null) return;

        bool full = PhotonNetwork.CurrentRoom.PlayerCount >= maxPlayers;

        PhotonNetwork.CurrentRoom.IsOpen = !full;
        PhotonNetwork.CurrentRoom.IsVisible = !full;

        Debug.Log("[NL] Room " + (full ? "LOCKED" : "OPEN") +
                  " players=" + PhotonNetwork.CurrentRoom.PlayerCount);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        joining = false;
        Debug.LogError("[NL] Disconnected: " + cause);

        CancelInvoke(nameof(Reconnect));
        Invoke(nameof(Reconnect), 1f);
    }

    private void Reconnect()
    {
        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("[NL] Reconnecting...");
            PhotonNetwork.ConnectUsingSettings();
        }
    }
}
