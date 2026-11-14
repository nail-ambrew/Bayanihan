using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The first screens of the game, as in the Figma "Landing", "Loading" and "User Auth" frames:
/// <list type="number">
/// <item>Loading: the flooded street and the orange bar while the game server is reached.</item>
/// <item>Landing: the logo and "Tap anywhere to start" (a saved session was resumed) or "... to login".</item>
/// <item>Quick Login for a returning player, otherwise Login, with Registration one tap away.</item>
/// </list>
/// Accounts live on Unity Gaming Services (see <see cref="AccountService"/>). Signing in leads to the main menu.
/// The whole UI is built in code: the only setup is this component on an empty object in the Account scene.
/// </summary>
public class AccountScreen : MonoBehaviour
{
    public enum Page { Login, Register }

    [Header("Flow")]
    [Tooltip("The page shown to a player who is not signed in, after the landing screen.")]
    public Page firstPage = Page.Login;
    [Tooltip("Sign a returning player straight back in with the session Unity cached on this device.")]
    public bool resumeSavedSession = true;

    [Header("Look")]
    public string cardTitle = "ELEV8 GAMES";

    // Card sizes, in Figma units.
    const float CardWidth = 256f, FieldWidth = 178f, FieldHeight = 30f;

    enum View { Connecting, Landing, Quick, Login, Register }

    Transform canvasRoot;
    RectTransform safe, card;
    GameObject connecting, landing, quickPanel, loginPanel, registerPanel, cardObject;
    LoadingScreen loadingBar;
    Text landingPrompt, cardHeading;
    InputField loginUser, loginPass, regUser, regEmail, regPass, regConfirm;
    Text loginStatus, registerStatus, quickStatus, quickName, quickWhen;
    Button loginButton, createButton, quickButton;
    View view;
    bool resumed, working;
    float connectStartedAt;

    // ------------------------------------------------------------------ lifecycle
    async void Start()
    {
        UiKit.EnsureEventSystem();
        BuildUI();
        Show(View.Connecting);
        connectStartedAt = Time.realtimeSinceStartup;

        string problem = null;
        try
        {
            await AccountService.InitializeAsync();
            if (resumeSavedSession) resumed = await AccountService.TryResumeAsync();
        }
        catch (AccountException e)
        {
            problem = e.Message;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            problem = "Can't reach the game server. Check your internet connection and try again.";
        }

        if (this == null) return;
        // Let the bar fill so the frame does not flash past.
        while (Time.realtimeSinceStartup - connectStartedAt < 0.8f) { await Task.Yield(); if (this == null) return; }
        Show(View.Landing);
        if (problem != null) UiKit.Notice(safe, "No connection", problem);
    }

    void Update()
    {
        if (view == View.Connecting && loadingBar != null)
            loadingBar.SetProgress(Mathf.Min(0.92f, (Time.realtimeSinceStartup - connectStartedAt) / 3f));
    }

    // ------------------------------------------------------------------ actions
    void LeaveLanding()
    {
        if (view != View.Landing || working) return;
        if (resumed && AccountService.Current != null) Show(View.Quick);
        else Show(firstPage == Page.Register ? View.Register : View.Login);
    }

    async void CreateAccount()
    {
        if (working) return;
        var problem = AccountRules.CheckRegistration(regUser.text, regEmail.text, regPass.text, regConfirm.text);
        if (problem != null) { SetStatus(registerStatus, problem, UiTheme.Error); return; }

        await Run(registerStatus, "Creating your account...",
            () => AccountService.RegisterAsync(regUser.text, regEmail.text, regPass.text, regConfirm.text),
            () => { regPass.text = regConfirm.text = ""; Continue(); });
    }

    async void LogIn()
    {
        if (working) return;
        var problem = AccountRules.CheckLogin(loginUser.text, loginPass.text);
        if (problem != null) { SetStatus(loginStatus, problem, UiTheme.Error); return; }

        await Run(loginStatus, "Logging in...",
            () => AccountService.LoginAsync(loginUser.text, loginPass.text),
            () => { loginPass.text = ""; Continue(); });
    }

    /// <summary>Quick Login's other link: forget this device's session and show the full login.</summary>
    async void OtherAccount()
    {
        if (working) return;
        working = true;
        SetStatus(quickStatus, "Signing out...", UiTheme.InkSoft);
        try
        {
            await AccountService.SignOutAsync();
            resumed = false;
            if (this == null) return;
            SetStatus(quickStatus, "", UiTheme.InkSoft);
            Show(View.Login);
        }
        catch (Exception e)
        {
            // Without this the form would stay locked on "Signing out..." for good.
            Debug.LogWarning("[Account] Sign out failed: " + e.Message);
            if (this != null) SetStatus(quickStatus, "Couldn't sign out. Check your connection and try again.", UiTheme.Error);
        }
        finally { working = false; }
    }

    void ForgotPassword()
    {
        UiKit.Notice(safe, "Forgot password?",
            "Passwords can't be reset from the game yet. Send your username to the ELEV8 GAMES team and they will " +
            "help you get back in. Your UID is shown in Settings once you are signed in.");
    }

    /// <summary>Quick Login's button: the saved session is already signed in, so this only moves on. Not while busy.</summary>
    void Continue()
    {
        if (working || SceneFlow.IsLoading) return;
        SceneFlow.Load(SceneFlow.Home);
    }

    /// <summary>Locks the form while a request is out, then either moves on or shows why it failed.</summary>
    async Task Run(Text status, string busyText, Func<Task<AccountRecord>> call, Action onSuccess)
    {
        working = true;
        SetInteractable(false);
        SetStatus(status, busyText, UiTheme.InkSoft);
        try
        {
            await call();
            if (this == null) return;
            SetStatus(status, "", UiTheme.InkSoft);
            onSuccess();
        }
        catch (AccountException e)
        {
            if (this == null) return;
            SetStatus(status, e.Message, UiTheme.Error);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            if (this == null) return;
            SetStatus(status, "Something went wrong. Try again.", UiTheme.Error);
        }
        finally
        {
            working = false;
            if (this != null) SetInteractable(true);
        }
    }

    // ------------------------------------------------------------------ views
    void Show(View next)
    {
        view = next;
        connecting.SetActive(next == View.Connecting);
        landing.SetActive(next == View.Landing);
        bool onCard = next == View.Quick || next == View.Login || next == View.Register;
        cardObject.SetActive(onCard);
        quickPanel.SetActive(next == View.Quick);
        loginPanel.SetActive(next == View.Login);
        registerPanel.SetActive(next == View.Register);

        float height = next switch
        {
            View.Register => 300f,
            View.Login => 262f,
            _ => 190f,
        };
        card.sizeDelta = new Vector2(CardWidth, height);

        if (next == View.Landing)
            landingPrompt.text = (InputHelper.TouchActive ? "Tap" : "Click") + " anywhere to " +
                                 (resumed && AccountService.Current != null ? "start" : "login");
        if (next == View.Quick) FillQuick();
        if (next == View.Login) { SetStatus(loginStatus, "", UiTheme.InkSoft); Focus(loginUser); }
        if (next == View.Register) { SetStatus(registerStatus, "", UiTheme.InkSoft); Focus(regUser); }
    }

    void Back()
    {
        if (working) return;
        if (view == View.Register) Show(View.Login);
        else Show(View.Landing);
    }

    void FillQuick()
    {
        var a = AccountService.Current;
        quickName.text = a != null ? a.username : "";
        string ago = AccountRules.TimeAgo(AccountService.PreviousLoginAt, DateTime.UtcNow);
        quickWhen.text = ago != null ? "Last login " + ago : "First login on this device";
        SetStatus(quickStatus, "", UiTheme.InkSoft);
    }

    static void SetStatus(Text status, string text, Color color)
    {
        if (status == null) return;
        status.color = color;
        status.text = text;
    }

    void SetInteractable(bool on)
    {
        foreach (var f in new[] { loginUser, loginPass, regUser, regEmail, regPass, regConfirm }) f.interactable = on;
        loginButton.interactable = on;
        createButton.interactable = on;
        quickButton.interactable = on;
    }

    static void Focus(InputField field)
    {
        // On a phone, selecting a field opens the keyboard over the card before the player has read it.
        if (InputHelper.TouchActive) return;
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(field.gameObject);
    }

    // ------------------------------------------------------------------ building
    void BuildUI()
    {
        safe = UiKit.NewCanvas("AccountCanvas", 0, out var canvas);
        canvasRoot = canvas.transform;
        UiKit.Background(canvasRoot);

        // Loading frame while the server is reached.
        var loadingRoot = UiKit.Rect("Connecting", safe);
        UiKit.Stretch(loadingRoot);
        loadingBar = loadingRoot.gameObject.AddComponent<LoadingScreen>();
        loadingBar.Build(loadingRoot, loadingRoot, "Connecting to the game server...");
        connecting = loadingRoot.gameObject;

        // Landing: the whole screen is the button.
        var landingRoot = UiKit.Image("Landing", safe, new Color(0, 0, 0, 0));
        UiKit.Stretch(landingRoot.rectTransform);
        landingRoot.raycastTarget = true;
        var tap = landingRoot.gameObject.AddComponent<Button>();
        tap.transition = Selectable.Transition.None;
        tap.onClick.AddListener(LeaveLanding);
        UiKit.Logo(landingRoot.transform, UiKit.TopLeft, new Vector2(14, -10));
        UiKit.Version(landingRoot.transform);
        landingPrompt = UiKit.Label("Prompt", landingRoot.transform, "", UiTheme.BodySize + 2, UiTheme.White, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(landingPrompt.rectTransform, UiKit.Bottom, new Vector2(0, 92), new Vector2(400, 20));
        landingPrompt.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.6f);
        landingPrompt.gameObject.AddComponent<Pulse>();
        landing = landingRoot.gameObject;

        // The sign-in card, with the logo and version behind it as on the landing frame.
        var cardScreen = UiKit.Rect("CardScreen", safe);
        UiKit.Stretch(cardScreen);
        UiKit.Logo(cardScreen, UiKit.TopLeft, new Vector2(14, -10));
        UiKit.Version(cardScreen);
        cardObject = cardScreen.gameObject;

        var cardImg = UiKit.Panel("Card", cardScreen, UiTheme.White, UiTheme.RadiusBoard);
        card = cardImg.rectTransform;
        UiKit.Place(card, UiKit.Center, new Vector2(0, -4), new Vector2(CardWidth, 262));

        UiKit.BackButton(card, UiKit.TopLeft, new Vector2(10, -8), Back, 18f);

        cardHeading = UiKit.Label("Title", card, cardTitle, 17, UiTheme.Ink, TextAnchor.MiddleCenter, heading: true);
        UiKit.Place(cardHeading.rectTransform, UiKit.Top, new Vector2(8, -10), new Vector2(170, 24));

        BuildQuick();
        BuildLogin();
        BuildRegister();
    }

    RectTransform NewPanel(string name)
    {
        var rt = UiKit.Rect(name, card);
        UiKit.Stretch(rt);
        return rt;
    }

    InputField Field(Transform panel, string placeholder, float y, InputField.ContentType type, int limit)
    {
        var f = UiKit.Field(panel, placeholder, type, limit);
        UiKit.Place((RectTransform)f.transform, UiKit.Top, new Vector2(0, y), new Vector2(FieldWidth, FieldHeight));
        return f;
    }

    Button WideButton(Transform panel, string text, float y, UnityEngine.Events.UnityAction onClick)
    {
        var b = UiKit.Button(panel, text, ButtonStyle.Primary, onClick, UiTheme.ButtonSize + 1);
        UiKit.Place((RectTransform)b.transform, UiKit.Top, new Vector2(0, y), new Vector2(FieldWidth, FieldHeight));
        return b;
    }

    Text Status(Transform panel, float y)
    {
        var t = UiKit.Label("Status", panel, "", UiTheme.SmallSize + 1, UiTheme.Error, TextAnchor.UpperCenter);
        UiKit.Place(t.rectTransform, UiKit.Top, new Vector2(0, y), new Vector2(FieldWidth + 20, 18));
        t.Shrink(5);
        return t;
    }

    void Link(Transform panel, string text, float y, Color color, UnityEngine.Events.UnityAction onClick, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var b = UiKit.Button(panel, text, ButtonStyle.Link, onClick, out var label, UiTheme.BodySize + 1);
        label.color = color;
        label.alignment = align;
        UiKit.Place((RectTransform)b.transform, UiKit.Top, new Vector2(0, y), new Vector2(FieldWidth, 16));
    }

    void BuildQuick()
    {
        var panel = NewPanel("QuickLogin");
        quickPanel = panel.gameObject;

        // The saved account, in an orange-rimmed box like a field.
        var box = UiKit.Panel("Account", panel, UiTheme.Orange, UiTheme.RadiusSmall);
        UiKit.Place(box.rectTransform, UiKit.Top, new Vector2(0, -48), new Vector2(FieldWidth, 36));
        var fill = UiKit.Panel("Fill", box.transform, UiTheme.White, UiTheme.RadiusSmall - 1f);
        UiKit.Inset(fill.rectTransform, 1f);
        quickName = UiKit.Label("Name", box.transform, "", UiTheme.BodySize + 1, UiTheme.Ink, TextAnchor.LowerLeft);
        UiKit.Inset(quickName.rectTransform, 7, 2, 7, 16);
        quickWhen = UiKit.Label("When", box.transform, "", UiTheme.SmallSize, UiTheme.InkSoft, TextAnchor.UpperLeft);
        UiKit.Inset(quickWhen.rectTransform, 7, 20, 7, 2);

        quickButton = WideButton(panel, "Login", -94, Continue);
        quickButton.name = "Button_Continue";      // the full login form has its own Button_Login
        quickStatus = Status(panel, -128);
        Link(panel, "Log in to another account", -146, UiTheme.Ink, OtherAccount);
    }

    void BuildLogin()
    {
        var panel = NewPanel("Login");
        loginPanel = panel.gameObject;
        loginUser = Field(panel, "Enter username", -46, InputField.ContentType.Standard, AccountRules.UsernameMax);
        loginPass = Field(panel, "Enter password", -84, InputField.ContentType.Password, AccountRules.PasswordMax);
        loginButton = WideButton(panel, "Login", -122, LogIn);
        loginStatus = Status(panel, -155);
        Link(panel, "Forgot password?", -174, UiTheme.Ink, ForgotPassword, TextAnchor.MiddleLeft);

        var line = UiKit.Image("Divider", panel, UiTheme.Rule);
        UiKit.Place(line.rectTransform, UiKit.Top, new Vector2(0, -199), new Vector2(FieldWidth, UiTheme.RuleThickness));
        var orBack = UiKit.Image("OrBack", panel, UiTheme.White);
        UiKit.Place(orBack.rectTransform, UiKit.Top, new Vector2(0, -194), new Vector2(16, 10));
        var or = UiKit.Label("Or", orBack.transform, "or", UiTheme.SmallSize, UiTheme.Ink, TextAnchor.MiddleCenter);
        UiKit.Stretch(or.rectTransform);

        Link(panel, "Create new account", -214, UiTheme.OrangeDeep, () => { if (!working) Show(View.Register); });
        loginPass.onSubmit.AddListener(_ => LogIn());
    }

    void BuildRegister()
    {
        var panel = NewPanel("Registration");
        registerPanel = panel.gameObject;
        regUser = Field(panel, "Username", -44, InputField.ContentType.Standard, AccountRules.UsernameMax);
        regEmail = Field(panel, "Email address", -80, InputField.ContentType.EmailAddress, AccountRules.EmailMax);
        regPass = Field(panel, "Password", -116, InputField.ContentType.Password, AccountRules.PasswordMax);
        regConfirm = Field(panel, "Confirm Password", -152, InputField.ContentType.Password, AccountRules.PasswordMax);
        createButton = WideButton(panel, "Create", -192, CreateAccount);
        registerStatus = Status(panel, -225);
        Link(panel, "Already have an account?", -252, UiTheme.OrangeDeep, () => { if (!working) Show(View.Login); }, TextAnchor.MiddleLeft);
        regConfirm.onSubmit.AddListener(_ => CreateAccount());
    }
}

/// <summary>Fades a label gently in and out, for "Tap anywhere to start".</summary>
public class Pulse : MonoBehaviour
{
    public float speed = 2.2f;
    Graphic graphic;
    void Awake() => graphic = GetComponent<Graphic>();
    void Update()
    {
        if (graphic == null) return;
        var c = graphic.color;
        c.a = Mathf.Lerp(0.45f, 1f, (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f);
        graphic.color = c;
    }
}
