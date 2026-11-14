using System.Text.RegularExpressions;

/// <summary>
/// What the register and login forms accept, checked before anything is sent to Unity.
/// The username and password limits are Unity Authentication's own, so a form that passes here is not rejected
/// by the server for its shape. Every check returns null when the value is fine, or the line to show the player.
/// </summary>
public static class AccountRules
{
    public const int UsernameMin = 3;
    public const int UsernameMax = 20;
    public const int PasswordMin = 8;
    public const int PasswordMax = 30;
    public const int EmailMax = 254;

    static readonly Regex UsernameChars = new Regex(@"^[A-Za-z0-9.\-@_]+$");
    static readonly Regex EmailShape = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    public static string CheckUsername(string username)
    {
        username = username?.Trim() ?? "";
        if (username.Length == 0) return "Enter a username.";
        if (username.Length < UsernameMin || username.Length > UsernameMax)
            return $"Username must be {UsernameMin}-{UsernameMax} characters.";
        if (!UsernameChars.IsMatch(username))
            return "Username can only use letters, numbers and . - @ _";
        return null;
    }

    public static string CheckEmail(string email)
    {
        email = email?.Trim() ?? "";
        if (email.Length == 0) return "Enter your email address.";
        if (email.Length > EmailMax || !EmailShape.IsMatch(email)) return "That email address doesn't look right.";
        return null;
    }

    public static string CheckPassword(string password)
    {
        password ??= "";
        if (password.Length == 0) return "Enter a password.";
        if (password.Length < PasswordMin || password.Length > PasswordMax)
            return $"Password must be {PasswordMin}-{PasswordMax} characters.";

        bool lower = false, upper = false, digit = false, symbol = false;
        foreach (char c in password)
        {
            if (char.IsLower(c)) lower = true;
            else if (char.IsUpper(c)) upper = true;
            else if (char.IsDigit(c)) digit = true;
            else if (!char.IsWhiteSpace(c)) symbol = true;
        }
        if (!(lower && upper && digit && symbol))
            return "Password needs an uppercase letter, a lowercase letter, a number and a symbol.";
        return null;
    }

    public static string CheckRegistration(string username, string email, string password, string confirm)
    {
        return CheckUsername(username)
            ?? CheckEmail(email)
            ?? CheckPassword(password)
            ?? (password == confirm ? null : "Passwords don't match.");
    }

    /// <summary>Login only checks that something was typed; whether it is right is the server's call.</summary>
    public static string CheckLogin(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username)) return "Enter your username.";
        // Unity's username/password sign-in only knows usernames, so an email address can never match.
        if (username.Contains("@")) return "Log in with your username. Signing in with an email address isn't available yet.";
        if (string.IsNullOrEmpty(password)) return "Enter your password.";
        return null;
    }

    /// <summary>"just now", "5 minutes ago", "1 hour ago", "3 days ago": how long ago an ISO-8601 UTC time was.</summary>
    public static string TimeAgo(string iso, System.DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(iso) ||
            !System.DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var then))
            return null;
        var span = nowUtc - then.ToUniversalTime();
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return Plural((int)span.TotalMinutes, "minute");
        if (span.TotalDays < 1) return Plural((int)span.TotalHours, "hour");
        if (span.TotalDays < 30) return Plural((int)span.TotalDays, "day");
        if (span.TotalDays < 365) return Plural((int)(span.TotalDays / 30), "month");
        return Plural((int)(span.TotalDays / 365), "year");
    }

    static string Plural(int n, string unit) => n + " " + unit + (n == 1 ? "" : "s") + " ago";

    /// <summary>Unity stores usernames lower-case; keep the same form in the account record.</summary>
    public static string NormalizeUsername(string username) => (username ?? "").Trim().ToLowerInvariant();

    public static string NormalizeEmail(string email) => (email ?? "").Trim().ToLowerInvariant();

    public const int UidDigits = 11;
    const ulong UidMin = 10_000_000_000UL;   // smallest 11-digit number, so a UID never starts with 0
    const ulong UidRange = 90_000_000_000UL; // 10000000000 .. 99999999999

    /// <summary>
    /// The player-facing 11-digit UID, derived from the Unity PlayerId with SHA-256. The same account always gets
    /// the same UID on every device, and nothing has to be stored for it to stay that way.
    /// </summary>
    public static string UidFromPlayerId(string playerId)
    {
        if (string.IsNullOrEmpty(playerId)) return null;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(playerId));
        ulong n = System.BitConverter.ToUInt64(hash, 0);
        return (UidMin + n % UidRange).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
