using System;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Vivox mission voice. All four roles share a team channel; transmission starts muted
/// and the UI can use SetPushToTalkHeld to implement push-to-talk.
/// </summary>
public sealed class RescueVoiceService : MonoBehaviour
{
    public static RescueVoiceService Instance { get; private set; }
    public bool IsLoggedIn { get; private set; }
    public bool IsInMissionChannel { get; private set; }
    public string CurrentChannel { get; private set; }
    public event Action<string> StatusChanged;
    bool serviceInitialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CreateAtStartup()
    {
        if (Instance != null) return;
        var root = new GameObject(nameof(RescueVoiceService));
        DontDestroyOnLoad(root);
        root.AddComponent<RescueVoiceService>();
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

    public async Task JoinMissionVoiceAsync(string sessionId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A session ID is required.", nameof(sessionId));
        try
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();

            if (!serviceInitialized)
            {
                await VivoxService.Instance.InitializeAsync();
                serviceInitialized = true;
            }
            if (!IsLoggedIn)
            {
                await VivoxService.Instance.LoginAsync(new LoginOptions
                {
                    DisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? $"Rescuer {AuthenticationService.Instance.PlayerId.Substring(0, 6)}"
                        : displayName.Trim()
                });
                IsLoggedIn = true;
            }

            // Keep the microphone closed while joining the channel; Vivox can otherwise
            // automatically switch transmission to a newly joined audio channel.
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);
            // Vivox channel names use a deterministic, session-scoped identifier so every
            // client in the same rescue session joins the same private team channel.
            var sessionToken = new string(sessionId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            CurrentChannel = "rescue" + sessionToken;
            await VivoxService.Instance.JoinGroupChannelAsync(CurrentChannel, ChatCapability.AudioOnly);
            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.None);
            IsInMissionChannel = true;
            StatusChanged?.Invoke("Team voice connected. Microphone is muted until push-to-talk is held.");
        }
        catch (Exception exception)
        {
            StatusChanged?.Invoke($"Voice could not connect: {exception.Message}");
            throw;
        }
    }

    public Task SetPushToTalkHeld(bool held)
    {
        if (!IsInMissionChannel) return Task.CompletedTask;
        return VivoxService.Instance.SetChannelTransmissionModeAsync(
            held ? TransmissionMode.All : TransmissionMode.None);
    }

    public void SetMicrophoneMuted(bool muted)
    {
        if (!IsLoggedIn) return;
        if (muted) VivoxService.Instance.MuteInputDevice();
        else VivoxService.Instance.UnmuteInputDevice();
    }

    public async Task LeaveVoiceAsync()
    {
        if (IsInMissionChannel && !string.IsNullOrEmpty(CurrentChannel))
            await VivoxService.Instance.LeaveChannelAsync(CurrentChannel);
        IsInMissionChannel = false;
        CurrentChannel = null;
        if (IsLoggedIn)
        {
            await VivoxService.Instance.LogoutAsync();
            IsLoggedIn = false;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
