using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

/// <summary>One player's account as it is kept in Cloud Save. <see cref="playerId"/> is the Unity Authentication
/// PlayerId: issued by Unity when the account is created and never changes. <see cref="uid"/> is the 11-digit
/// number players see, derived from it by <see cref="AccountRules.UidFromPlayerId"/>.</summary>
[Serializable]
public class AccountRecord
{
    public string uid;           // 11 digits, e.g. 48213907561
    public string playerId;      // Unity Authentication PlayerId
    public string username;
    public string email;
    public string createdAt;     // ISO-8601 UTC
    public string lastLoginAt;   // ISO-8601 UTC
    public int loginCount;
}

/// <summary>One sign-in, from login to sign-out. A session with no <see cref="endedAt"/> was closed without
/// signing out (the game was quit, or crashed).</summary>
[Serializable]
public class LoginSession
{
    public string sessionId;
    public string uid;
    public string method;        // "register" | "password" | "resume"
    public string startedAt;
    public string endedAt;
    public string platform;
    public string device;
    public string gameVersion;
}

/// <summary>Validation and server failures, worded for the player.</summary>
public class AccountException : Exception
{
    public AccountException(string message, Exception inner = null) : base(message, inner) { }
}

/// <summary>
/// Account creation and login on Unity Gaming Services.
/// <list type="bullet">
/// <item>Unity Authentication holds the credentials: username + password (hashed by Unity, never stored by us),
/// and issues the PlayerId the 11-digit UID is derived from.</item>
/// <item>Cloud Save holds the account record (<see cref="AccountKey"/>) and the login history
/// (<see cref="SessionsKey"/>), both in the player's private Default bucket, so only that player can read them.</item>
/// </list>
/// Unity caches a session token on the device, so a player who did not sign out is let straight back in by
/// <see cref="TryResumeAsync"/>.
/// </summary>
public static class AccountService
{
    public const string AccountKey = "account";
    public const string SessionsKey = "login_sessions";
    public const int MaxSessionsKept = 20;

    /// <summary>The signed-in player's account, or null.</summary>
    public static AccountRecord Current { get; private set; }

    /// <summary>The session that started at this sign-in, or null.</summary>
    public static LoginSession CurrentSession { get; private set; }

    /// <summary>When this player last signed in before now (ISO-8601 UTC), or null on their first sign-in.
    /// The quick-login card shows it as "Last login 1 hour ago".</summary>
    public static string PreviousLoginAt { get; private set; }

    /// <summary>The signed-in player's 11-digit UID, or null.</summary>
    public static string Uid => Current?.uid;

    public static bool IsSignedIn => Current != null
        && UnityServices.State == ServicesInitializationState.Initialized
        && AuthenticationService.Instance.IsSignedIn;

    static Task initializing;

    public static async Task InitializeAsync()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized) return;
        initializing ??= UnityServices.InitializeAsync();
        try
        {
            await initializing;
        }
        catch (Exception e)
        {
            initializing = null;
            throw new AccountException("Can't reach the game server. Check your internet connection and try again.", e);
        }
    }

    // ------------------------------------------------------------------ register / login / resume
    public static async Task<AccountRecord> RegisterAsync(string username, string email, string password, string confirm)
    {
        var problem = AccountRules.CheckRegistration(username, email, password, confirm);
        if (problem != null) throw new AccountException(problem);

        await InitializeAsync();
        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn) auth.SignOut(true);

        try
        {
            await auth.SignUpWithUsernamePasswordAsync(username.Trim(), password);
        }
        catch (RequestFailedException e)
        {
            throw new AccountException(Describe(e, registering: true), e);
        }

        try
        {
            // The player name is what other players see; it is optional, so a refusal does not stop the sign-up.
            await auth.UpdatePlayerNameAsync(username.Trim());
        }
        catch (RequestFailedException e)
        {
            Debug.LogWarning($"[Account] Could not set the player name: {e.Message}");
        }

        var now = Now();
        var account = new AccountRecord
        {
            uid = AccountRules.UidFromPlayerId(auth.PlayerId),
            playerId = auth.PlayerId,
            username = AccountRules.NormalizeUsername(username),
            email = AccountRules.NormalizeEmail(email),
            createdAt = now,
            lastLoginAt = now,
            loginCount = 1,
        };
        PreviousLoginAt = null;
        await StartSessionAsync(account, new List<LoginSession>(), "register");
        return account;
    }

    public static async Task<AccountRecord> LoginAsync(string username, string password)
    {
        var problem = AccountRules.CheckLogin(username, password);
        if (problem != null) throw new AccountException(problem);

        await InitializeAsync();
        var auth = AuthenticationService.Instance;
        if (auth.IsSignedIn) auth.SignOut(true);

        try
        {
            await auth.SignInWithUsernamePasswordAsync(username.Trim(), password);
        }
        catch (RequestFailedException e)
        {
            throw new AccountException(Describe(e, registering: false), e);
        }

        return await LoadAndStartSessionAsync("password");
    }

    /// <summary>Signs the player back in from the session token Unity cached on this device. False when there is
    /// none, it has expired, or it does not belong to a username/password account.</summary>
    public static async Task<bool> TryResumeAsync()
    {
        await InitializeAsync();
        var auth = AuthenticationService.Instance;
        if (!auth.IsSignedIn)
        {
            // Without a cached token this call would create a brand-new anonymous player, so never make it blind.
            if (!auth.SessionTokenExists) return false;
            try
            {
                await auth.SignInAnonymouslyAsync();
            }
            catch (RequestFailedException e)
            {
                Debug.Log($"[Account] Saved session could not be resumed ({e.ErrorCode}): {e.Message}");
                auth.ClearSessionToken();
                return false;
            }
        }

        if (string.IsNullOrEmpty(auth.PlayerInfo?.Username))
        {
            // An anonymous player, not one of our accounts.
            auth.SignOut(true);
            return false;
        }

        try
        {
            await LoadAndStartSessionAsync("resume");
            return true;
        }
        catch (AccountException e)
        {
            Debug.LogWarning($"[Account] Signed in, but the account could not be loaded: {e.Message}");
            auth.SignOut();
            return false;
        }
    }

    /// <summary>Closes the current session in the history, then signs out and forgets the cached token, so the
    /// next launch asks for the password again.</summary>
    public static async Task SignOutAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized) return;
        var auth = AuthenticationService.Instance;

        if (auth.IsSignedIn && CurrentSession != null)
        {
            try
            {
                var sessions = await LoadSessionsAsync();
                var mine = sessions.Find(s => s.sessionId == CurrentSession.sessionId);
                if (mine != null)
                {
                    mine.endedAt = Now();
                    await CloudSaveService.Instance.Data.Player.SaveAsync(
                        new Dictionary<string, object> { { SessionsKey, sessions } });
                }
            }
            catch (Exception e)
            {
                // Signing out must still work offline; the session just stays open in the history.
                Debug.LogWarning($"[Account] Could not record the end of the session: {e.Message}");
            }
        }

        if (auth.IsSignedIn) auth.SignOut(true);
        else auth.ClearSessionToken();
        Current = null;
        CurrentSession = null;
        PreviousLoginAt = null;
    }

    /// <summary>This player's sign-ins, newest first.</summary>
    public static async Task<List<LoginSession>> LoadSessionsAsync()
    {
        try
        {
            var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { SessionsKey });
            var list = items.TryGetValue(SessionsKey, out var item)
                ? item.Value.GetAs<List<LoginSession>>() ?? new List<LoginSession>()
                : new List<LoginSession>();
            return list;
        }
        catch (RequestFailedException e)
        {
            throw new AccountException("Couldn't load your login history.", e);
        }
    }

    // ------------------------------------------------------------------ internals
    static async Task<AccountRecord> LoadAndStartSessionAsync(string method)
    {
        var auth = AuthenticationService.Instance;
        AccountRecord account = null;
        List<LoginSession> sessions;
        try
        {
            var items = await CloudSaveService.Instance.Data.Player.LoadAsync(
                new HashSet<string> { AccountKey, SessionsKey });
            if (items.TryGetValue(AccountKey, out var a)) account = a.Value.GetAs<AccountRecord>();
            sessions = items.TryGetValue(SessionsKey, out var s)
                ? s.Value.GetAs<List<LoginSession>>() ?? new List<LoginSession>()
                : new List<LoginSession>();
        }
        catch (RequestFailedException e)
        {
            throw new AccountException("Signed in, but your account data couldn't be loaded. Try again.", e);
        }

        // The sign-up can succeed and the first save fail (the connection drops in between). Rebuild the record
        // rather than lock the player out; only the email is lost, and it can be asked for again.
        account ??= new AccountRecord
        {
            username = AccountRules.NormalizeUsername(auth.PlayerInfo?.Username),
            email = "",
            createdAt = auth.PlayerInfo?.CreatedAt?.ToUniversalTime().ToString("o") ?? Now(),
        };

        // The UID always comes from Authentication, never from what was saved.
        account.playerId = auth.PlayerId;
        account.uid = AccountRules.UidFromPlayerId(auth.PlayerId);
        // Accounts made before the 11-digit UID stored the raw PlayerId in their history; bring those up to date.
        foreach (var s in sessions) s.uid = account.uid;
        PreviousLoginAt = string.IsNullOrEmpty(account.lastLoginAt) ? null : account.lastLoginAt;
        account.lastLoginAt = Now();
        account.loginCount++;

        await StartSessionAsync(account, sessions, method);
        return account;
    }

    static async Task StartSessionAsync(AccountRecord account, List<LoginSession> sessions, string method)
    {
        var session = new LoginSession
        {
            sessionId = Guid.NewGuid().ToString("N"),
            uid = account.uid,
            method = method,
            startedAt = account.lastLoginAt,
            platform = Application.platform.ToString(),
            device = SystemInfo.deviceModel,
            gameVersion = Application.version,
        };
        sessions.Insert(0, session);
        if (sessions.Count > MaxSessionsKept) sessions.RemoveRange(MaxSessionsKept, sessions.Count - MaxSessionsKept);

        try
        {
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object>
            {
                { AccountKey, account },
                { SessionsKey, sessions },
            });
        }
        catch (RequestFailedException e)
        {
            throw new AccountException("Signed in, but your account couldn't be saved. Try again.", e);
        }

        Current = account;
        CurrentSession = session;
        Debug.Log($"[Account] Signed in as {account.username} (UID {account.uid}) via {method}");
    }

    static string Describe(RequestFailedException e, bool registering)
    {
        Debug.LogWarning($"[Account] {(registering ? "Sign-up" : "Login")} failed ({e.ErrorCode}): {e.Message}");

        if (e.ErrorCode == CommonErrorCodes.TransportError)
            return "Can't reach the game server. Check your internet connection and try again.";
        if (e.ErrorCode == CommonErrorCodes.TooManyRequests)
            return "Too many attempts. Wait a moment and try again.";
        if (e.ErrorCode == AuthenticationErrorCodes.BannedUser)
            return "This account has been suspended.";
        if (registering && e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            return "That username is already taken.";
        // "usernamepassword external id provider is not available: PERMISSION_DENIED" — the identity provider
        // has not been added to this project/environment in the Unity Dashboard.
        if (e.Message != null && e.Message.IndexOf("provider is not", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Username/password accounts are switched off for this game " +
                   "(Unity Dashboard > Player Authentication > Identity providers).";
        return registering
            ? "Couldn't create the account. Check the details and try again."
            : "Wrong username or password.";
    }

    static string Now() => DateTime.UtcNow.ToString("o");
}
