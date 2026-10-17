using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

/// <summary>
/// Local entry point for a four-player, host-led rescue session over Unity Relay.
/// Put this on a menu object or access the persistent Instance from the join/create UI.
/// </summary>
public sealed class RescueSessionService : MonoBehaviour
{
    public static RescueSessionService Instance { get; private set; }

    public ISession CurrentSession { get; private set; }
    public string JoinCode => CurrentSession?.Code;
    public bool IsHost => CurrentSession != null && CurrentSession.IsHost;
    public event Action<ISession> SessionConnected;
    public event Action SessionDisconnected;
    public event Action<string> StatusChanged;

    bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CreateAtStartup()
    {
        if (Instance != null) return;
        var root = new GameObject(nameof(RescueSessionService));
        DontDestroyOnLoad(root);
        root.AddComponent<RescueSessionService>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<ISession> CreateMissionAsync()
    {
        if (busy) throw new InvalidOperationException("A session operation is already running.");
        if (CurrentSession != null) throw new InvalidOperationException("Leave the current mission before creating another.");

        busy = true;
        try
        {
            EnsureNetworkManager();
            await InitializeServicesAsync();
            var options = new SessionOptions
            {
                MaxPlayers = CrewRoleInfo.CrewSize,
                Name = "Bayanihan Rescue"
            }
                .WithRelayNetwork()
                .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.DTLS });

            CurrentSession = await MultiplayerService.Instance.CreateSessionAsync(options);
            StatusChanged?.Invoke($"Mission ready. Share code: {CurrentSession.Code}");
            await TryConnectVoiceAsync(CurrentSession);
            SessionConnected?.Invoke(CurrentSession);
            return CurrentSession;
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Could not create mission: {exception.Message}");
            throw;
        }
        finally { busy = false; }
    }

    public async Task<ISession> JoinMissionAsync(string joinCode)
    {
        if (busy) throw new InvalidOperationException("A session operation is already running.");
        if (CurrentSession != null) throw new InvalidOperationException("Leave the current mission before joining another.");
        if (string.IsNullOrWhiteSpace(joinCode)) throw new ArgumentException("Enter a mission join code.", nameof(joinCode));

        busy = true;
        try
        {
            EnsureNetworkManager();
            await InitializeServicesAsync();
            CurrentSession = await MultiplayerService.Instance.JoinSessionByCodeAsync(joinCode.Trim());
            StatusChanged?.Invoke("Joined the rescue mission.");
            await TryConnectVoiceAsync(CurrentSession);
            SessionConnected?.Invoke(CurrentSession);
            return CurrentSession;
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Could not join mission: {exception.Message}");
            throw;
        }
        finally { busy = false; }
    }

    public async Task LeaveMissionAsync()
    {
        if (CurrentSession == null) return;

        var session = CurrentSession;
        CurrentSession = null;
        try
        {
            if (RescueVoiceService.Instance != null)
                await RescueVoiceService.Instance.LeaveVoiceAsync();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Voice cleanup failed while leaving the mission: {exception.Message}");
        }

        try
        {
            await session.LeaveAsync();
            StatusChanged?.Invoke("Left the rescue mission.");
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Session closed locally; cleanup reported: {exception.Message}");
            throw;
        }
        finally { SessionDisconnected?.Invoke(); }
    }

    static async Task InitializeServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    static void EnsureNetworkManager()
    {
        if (NetworkManager.Singleton != null) return;

        var root = new GameObject("Bayanihan Network Manager");
        DontDestroyOnLoad(root);
        var transport = root.AddComponent<UnityTransport>();
        var manager = root.AddComponent<NetworkManager>();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.RunInBackground = true;
    }

    static async Task TryConnectVoiceAsync(ISession session)
    {
        if (RescueVoiceService.Instance == null) return;
        var playerId = AuthenticationService.Instance.PlayerId;
        var displayName = string.IsNullOrEmpty(playerId)
            ? "Rescue Crew"
            : $"Rescue {playerId.Substring(0, Math.Min(6, playerId.Length))}";
        try
        {
            await RescueVoiceService.Instance.JoinMissionVoiceAsync(session.Id, displayName);
        }
        catch (Exception exception)
        {
            // A voice service outage should not prevent the crew from entering the mission.
            Debug.LogWarning($"Mission connected, but team voice is unavailable: {exception.Message}");
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
