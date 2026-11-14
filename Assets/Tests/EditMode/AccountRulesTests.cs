using NUnit.Framework;

/// <summary>What the register and login forms let through before anything reaches Unity Authentication.</summary>
public class AccountRulesTests
{
    const string GoodPassword = "Bangka#2026";

    [Test]
    public void A_complete_registration_passes()
    {
        Assert.IsNull(AccountRules.CheckRegistration("juan.dc", "juan@example.com", GoodPassword, GoodPassword));
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("ab", ExpectedResult = false)]
    [TestCase("abc", ExpectedResult = true)]
    [TestCase("abcdefghijklmnopqrst", ExpectedResult = true)]   // 20
    [TestCase("abcdefghijklmnopqrstu", ExpectedResult = false)] // 21
    [TestCase("rescuer_01", ExpectedResult = true)]
    [TestCase("a.b-c@d_e", ExpectedResult = true)]
    [TestCase("juan dela", ExpectedResult = false)]
    [TestCase("juan!", ExpectedResult = false)]
    [TestCase("ñino", ExpectedResult = false)]
    public bool Usernames_follow_unity_authentication_limits(string username)
    {
        return AccountRules.CheckUsername(username) == null;
    }

    [TestCase("juan@example.com", ExpectedResult = true)]
    [TestCase("  juan@example.com ", ExpectedResult = true)]
    [TestCase("juan@example", ExpectedResult = false)]
    [TestCase("juan.example.com", ExpectedResult = false)]
    [TestCase("juan @example.com", ExpectedResult = false)]
    [TestCase("", ExpectedResult = false)]
    public bool Email_must_look_like_an_address(string email)
    {
        return AccountRules.CheckEmail(email) == null;
    }

    [TestCase(GoodPassword, ExpectedResult = true)]
    [TestCase("Bk#2026", ExpectedResult = false)]       // 7 characters
    [TestCase("bangka#2026", ExpectedResult = false)]   // no uppercase
    [TestCase("BANGKA#2026", ExpectedResult = false)]   // no lowercase
    [TestCase("Bangka#Bangka", ExpectedResult = false)] // no number
    [TestCase("Bangka2026", ExpectedResult = false)]    // no symbol
    [TestCase("Aa1!aaaaaaaaaaaaaaaaaaaaaaaaaa", ExpectedResult = true)]   // 30
    [TestCase("Aa1!aaaaaaaaaaaaaaaaaaaaaaaaaaa", ExpectedResult = false)] // 31
    public bool Passwords_follow_unity_authentication_limits(string password)
    {
        return AccountRules.CheckPassword(password) == null;
    }

    [Test]
    public void Confirm_password_must_match()
    {
        Assert.AreEqual("Passwords don't match.",
            AccountRules.CheckRegistration("juan.dc", "juan@example.com", GoodPassword, GoodPassword + "x"));
    }

    [Test]
    public void The_first_problem_on_the_form_is_the_one_reported()
    {
        var problem = AccountRules.CheckRegistration("ab", "not-an-email", "short", "other");
        Assert.AreEqual(AccountRules.CheckUsername("ab"), problem);
    }

    [Test]
    public void Login_only_needs_both_fields_filled()
    {
        Assert.IsNotNull(AccountRules.CheckLogin("", "x"));
        Assert.IsNotNull(AccountRules.CheckLogin("juan", ""));
        Assert.IsNull(AccountRules.CheckLogin("juan", "anything"));
    }

    [TestCase("Xb5mQ1rT9vYk2LpZ8aWc3dEf4gHi")]
    [TestCase("a")]
    [TestCase("0000000000000000000000000000")]
    public void Uid_is_eleven_digits_with_no_leading_zero(string playerId)
    {
        var uid = AccountRules.UidFromPlayerId(playerId);
        Assert.AreEqual(AccountRules.UidDigits, uid.Length, uid);
        StringAssert.IsMatch("^[1-9][0-9]{10}$", uid);
    }

    [Test]
    public void Uid_is_the_same_every_time_for_the_same_player()
    {
        Assert.AreEqual(AccountRules.UidFromPlayerId("Xb5mQ1rT9vYk2LpZ8aWc3dEf4gHi"),
                        AccountRules.UidFromPlayerId("Xb5mQ1rT9vYk2LpZ8aWc3dEf4gHi"));
    }

    [Test]
    public void Different_players_get_different_uids()
    {
        var seen = new System.Collections.Generic.HashSet<string>();
        for (int i = 0; i < 10000; i++)
            Assert.IsTrue(seen.Add(AccountRules.UidFromPlayerId("player-" + i)), "UID repeated at player " + i);
    }

    [Test]
    public void No_player_id_means_no_uid()
    {
        Assert.IsNull(AccountRules.UidFromPlayerId(null));
        Assert.IsNull(AccountRules.UidFromPlayerId(""));
    }

    [Test]
    public void Usernames_and_emails_are_stored_lower_case()
    {
        Assert.AreEqual("juan.dc", AccountRules.NormalizeUsername("  Juan.DC "));
        Assert.AreEqual("juan@example.com", AccountRules.NormalizeEmail("Juan@Example.COM "));
    }
}
