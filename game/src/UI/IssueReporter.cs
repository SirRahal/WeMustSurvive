using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Godot;

namespace BeltAndBarricade.UI;

/// <summary>
/// Dev-build tool that files a GitHub issue from inside the game. A small "Report issue" button sits at the top of
/// every screen (F8 also opens it). Opening it grabs a screenshot of the current frame first, so the shot shows what
/// you were looking at, not the dialog. With "Add Screenshot" ticked, the PNG is committed to the
/// <see cref="ScreenshotBranch"/> branch and embedded in the issue.
/// Auth: WMS_GITHUB_TOKEN / GITHUB_TOKEN / GH_TOKEN, otherwise the GitHub login git already has (git credential fill).
/// </summary>
public partial class IssueReporter : CanvasLayer
{
    public const string Repo = "SirRahal/WeMustSurvive";
    public const string ScreenshotBranch = "issue-screenshots";
    private const int MaxShotWidth = 1920;

    private static readonly (string Label, string? GitHubLabel)[] Kinds =
    {
        ("Bug", "bug"),
        ("Feature / idea", "enhancement"),
        ("Task", null),
    };

    /// <summary>Who the issue is assigned to: display name -> GitHub username (must be a repo collaborator).</summary>
    private static readonly (string Name, string Login)[] Assignees =
    {
        ("Sari", "SirRahal"),
        ("Kyle", ""), // TODO: Kyle's GitHub username (not email). Until set, Kyle's issues are filed unassigned.
    };

    /// <summary>Remembered between reports so you don't have to re-pick it every time.</summary>
    private static int _lastAssignee;

    /// <summary>True while the dialog is open; the camera ignores keyboard and edge panning meanwhile.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>Describes the current screen / game state for the issue body.</summary>
    public Func<string>? Context { get; set; }

    private Button _openButton = null!;
    private Control _dialog = null!;
    private LineEdit _title = null!;
    private OptionButton _kind = null!;
    private OptionButton _assignee = null!;
    private TextEdit _body = null!;
    private CheckBox _attach = null!;
    private TextureRect _preview = null!;
    private Label _status = null!;
    private Button _submit = null!;
    private Button _cancel = null!;
    private Button _openLink = null!;
    private Image? _shot;
    private string _context = "";
    private string? _issueUrl;
    private bool _busy;

    public override void _Ready()
    {
        Layer = 100;
        ProcessMode = ProcessModeEnum.Always;

        _openButton = new Button
        {
            Text = "Report issue (F8)",
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "File a GitHub issue (dev builds only)",
            // Right edge, halfway down: clear of the HUD's bars, minimap and pop-ups.
            AnchorLeft = 1, AnchorRight = 1, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -162, OffsetRight = -12, OffsetTop = -13, OffsetBottom = 13,
        };
        _openButton.AddThemeFontSizeOverride("font_size", 13);
        _openButton.AddThemeStyleboxOverride("normal", UiKit.Box(new Color(0.08f, 0.09f, 0.12f, 0.75f), UiKit.Dim with { A = 0.5f }, 1, 4));
        _openButton.AddThemeStyleboxOverride("hover", UiKit.Box(new Color(0.15f, 0.2f, 0.27f, 0.9f), UiKit.Accent, 1, 4));
        _openButton.AddThemeStyleboxOverride("pressed", UiKit.Box(new Color(0.06f, 0.07f, 0.09f, 0.9f), UiKit.Accent, 1, 4));
        _openButton.Pressed += () => _ = Open();
        AddChild(_openButton);

        BuildDialog();
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F8 && !IsOpen)
        {
            GetViewport().SetInputAsHandled();
            _ = Open();
        }
        else if (key.Keycode == Key.Escape && IsOpen)
        {
            GetViewport().SetInputAsHandled();
            if (!_busy) Close();
        }
    }

    // ------------------------------------------------------------------ dialog

    private void BuildDialog()
    {
        _dialog = new Control { Visible = false, MouseFilter = Control.MouseFilterEnum.Stop };
        _dialog.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(_dialog);

        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Stop };
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _dialog.AddChild(dim);

        var column = UiKit.Column(10);
        var heading = UiKit.Label("Report an issue", 26, UiKit.Accent);
        heading.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(heading);

        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 8);
        _title = new LineEdit { PlaceholderText = "Short title (required)", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _title.TextChanged += _ => RefreshSubmit();
        _title.TextSubmitted += _ => _body.GrabFocus();
        titleRow.AddChild(_title);
        _kind = new OptionButton();
        foreach (var (label, _) in Kinds) _kind.AddItem(label);
        titleRow.AddChild(_kind);
        column.AddChild(titleRow);

        var assignRow = new HBoxContainer();
        assignRow.AddThemeConstantOverride("separation", 8);
        assignRow.AddChild(UiKit.Label("Assign to", 16));
        _assignee = new OptionButton();
        foreach (var (name, _) in Assignees) _assignee.AddItem(name);
        _assignee.ItemSelected += i => _lastAssignee = (int)i;
        assignRow.AddChild(_assignee);
        column.AddChild(assignRow);

        _body = new TextEdit
        {
            PlaceholderText = "What happened? What did you expect? Steps to reproduce...",
            CustomMinimumSize = new Vector2(560, 160),
            WrapMode = TextEdit.LineWrappingMode.Boundary,
        };
        column.AddChild(_body);

        _attach = new CheckBox { Text = "Add Screenshot", ButtonPressed = true };
        _attach.Toggled += on => _preview.Visible = on && _shot != null;
        column.AddChild(_attach);

        _preview = new TextureRect
        {
            CustomMinimumSize = new Vector2(560, 315),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        column.AddChild(_preview);

        _status = UiKit.Label("", 15, UiKit.Dim, wrap: true);
        _status.CustomMinimumSize = new Vector2(560, 0);
        column.AddChild(_status);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 12);
        _cancel = UiKit.Button("Cancel", Close, width: 160);
        _submit = UiKit.Button("Create issue", () => _ = Submit(), width: 180);
        _openLink = UiKit.Button("Open issue", () => { if (_issueUrl != null) OS.ShellOpen(_issueUrl); }, width: 160);
        buttons.AddChild(_cancel);
        buttons.AddChild(_submit);
        buttons.AddChild(_openLink);
        column.AddChild(buttons);

        _dialog.AddChild(UiKit.Centered(UiKit.Panel(column, 600)));
    }

    private async Task Open()
    {
        if (IsOpen) return;
        IsOpen = true;

        // Capture the frame as the player sees it, without our own button on it.
        _openButton.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _shot = GetViewport().GetTexture().GetImage();
        if (_shot.GetWidth() > MaxShotWidth)
            _shot.Resize(MaxShotWidth, _shot.GetHeight() * MaxShotWidth / _shot.GetWidth(), Image.Interpolation.Bilinear);
        _context = SafeContext();

        _issueUrl = null;
        _title.Text = "";
        _body.Text = "";
        _kind.Selected = 0;
        _assignee.Selected = _lastAssignee;
        _title.Editable = _body.Editable = true;
        _kind.Disabled = _assignee.Disabled = _attach.Disabled = false;
        _preview.Texture = ImageTexture.CreateFromImage(_shot);
        _preview.Visible = _attach.ButtonPressed;
        _status.Text = "";
        _cancel.Text = "Cancel";
        _openLink.Visible = false;
        _submit.Visible = true;
        RefreshSubmit();
        _dialog.Visible = true;
        _title.GrabFocus();
    }

    private void Close()
    {
        _dialog.Visible = false;
        _openButton.Visible = true;
        _preview.Texture = null;
        _shot = null;
        IsOpen = false;
    }

    private void RefreshSubmit() => _submit.Disabled = _busy || string.IsNullOrWhiteSpace(_title.Text);

    private void SetBusy(bool busy, string status, Color? color = null)
    {
        _busy = busy;
        _status.Text = status;
        _status.AddThemeColorOverride("font_color", color ?? UiKit.Dim);
        _cancel.Disabled = busy;
        RefreshSubmit();
    }

    private string SafeContext()
    {
        try { return Context?.Invoke() ?? ""; }
        catch (Exception ex) { return $"(context unavailable: {ex.Message})"; }
    }

    // ------------------------------------------------------------------ GitHub

    private async Task Submit()
    {
        if (_busy || string.IsNullOrWhiteSpace(_title.Text)) return;
        string title = _title.Text.Trim();
        string description = _body.Text.Trim();
        string? label = Kinds[_kind.Selected].GitHubLabel;
        var (assigneeName, assigneeLogin) = Assignees[_assignee.Selected];
        byte[]? png = _attach.ButtonPressed && _shot != null ? _shot.SavePngToBuffer() : null;

        SetBusy(true, "Signing in to GitHub...");
        try
        {
            string token = await Task.Run(GetToken) ?? throw new InvalidOperationException(
                "No GitHub login found. Push to the repo once with git, or set the GITHUB_TOKEN environment variable.");
            using var gh = new GitHub(token);

            string? shotUrl = null;
            if (png != null)
            {
                SetBusy(true, "Uploading screenshot...");
                shotUrl = await UploadScreenshot(gh, png, title);
            }

            SetBusy(true, "Creating issue...");
            var issue = new JsonObject { ["title"] = title, ["body"] = BuildBody(description, shotUrl) };
            if (label != null) issue["labels"] = new JsonArray(label);
            if (assigneeLogin.Length > 0) issue["assignees"] = new JsonArray(assigneeLogin);
            var created = await gh.Send(HttpMethod.Post, $"repos/{Repo}/issues", issue);
            _issueUrl = created?["html_url"]?.GetValue<string>();
            int number = created?["number"]?.GetValue<int>() ?? 0;

            // GitHub silently drops assignees who aren't collaborators, so confirm it stuck.
            bool assigned = created?["assignees"] is JsonArray list &&
                            list.Any(a => string.Equals(a?["login"]?.GetValue<string>(), assigneeLogin, StringComparison.OrdinalIgnoreCase));
            string assignNote = assigned
                ? $" Assigned to {assigneeName}."
                : assigneeLogin.Length == 0
                    ? $" Not assigned: {assigneeName}'s GitHub username isn't set in IssueReporter.cs yet."
                    : $" Not assigned: {assigneeName} ({assigneeLogin}) isn't a collaborator yet (invite pending?).";
            SetBusy(false, $"Created issue #{number}.{assignNote} The link is copied to the clipboard.", assigned ? UiKit.Good : UiKit.Gold);
            if (_issueUrl != null) DisplayServer.ClipboardSet(_issueUrl);
            _title.Editable = _body.Editable = false;
            _kind.Disabled = _assignee.Disabled = _attach.Disabled = true;
            _submit.Visible = false;
            _openLink.Visible = _issueUrl != null;
            _cancel.Text = "Close";
            _cancel.GrabFocus();
        }
        catch (Exception ex)
        {
            SetBusy(false, "Couldn't create the issue: " + ex.Message, UiKit.Warn);
            GD.PushWarning($"IssueReporter: {ex}");
        }
    }

    private string BuildBody(string description, string? shotUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine(description.Length > 0 ? description : "_No description._");
        if (shotUrl != null) sb.AppendLine().AppendLine($"![screenshot]({shotUrl})");
        sb.AppendLine().AppendLine("<details><summary>Game context</summary>").AppendLine();
        sb.AppendLine("```");
        if (_context.Length > 0) sb.AppendLine(_context);
        sb.AppendLine($"Godot {Engine.GetVersionInfo()["string"]}, {OS.GetName()}, " +
                      $"window {DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}");
        sb.AppendLine("```").AppendLine().AppendLine("</details>").AppendLine();
        sb.AppendLine("_Filed from the in-game issue reporter._");
        return sb.ToString();
    }

    /// <summary>Commits the PNG to the screenshot branch (created from main on first use) and returns its raw URL.</summary>
    private static async Task<string> UploadScreenshot(GitHub gh, byte[] png, string title)
    {
        var branch = await gh.Send(HttpMethod.Get, $"repos/{Repo}/git/ref/heads/{ScreenshotBranch}", allowNotFound: true);
        if (branch == null)
        {
            var repo = await gh.Send(HttpMethod.Get, $"repos/{Repo}");
            string main = repo?["default_branch"]?.GetValue<string>() ?? "main";
            var head = await gh.Send(HttpMethod.Get, $"repos/{Repo}/git/ref/heads/{main}");
            await gh.Send(HttpMethod.Post, $"repos/{Repo}/git/refs", new JsonObject
            {
                ["ref"] = $"refs/heads/{ScreenshotBranch}",
                ["sha"] = head?["object"]?["sha"]?.GetValue<string>(),
            });
        }

        string path = $"screenshots/{DateTime.UtcNow:yyyy-MM-dd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.png";
        await gh.Send(HttpMethod.Put, $"repos/{Repo}/contents/{path}", new JsonObject
        {
            ["message"] = $"Screenshot for issue: {title}",
            ["content"] = Convert.ToBase64String(png),
            ["branch"] = ScreenshotBranch,
        });
        return $"https://raw.githubusercontent.com/{Repo}/{ScreenshotBranch}/{path}";
    }

    /// <summary>A token from the environment, or the github.com credential git already stores.</summary>
    private static string? GetToken()
    {
        foreach (var name in new[] { "WMS_GITHUB_TOKEN", "GITHUB_TOKEN", "GH_TOKEN" })
        {
            var value = System.Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "credential fill")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (git == null) return null;
            git.StandardInput.Write("protocol=https\nhost=github.com\n\n");
            git.StandardInput.Close();
            string output = git.StandardOutput.ReadToEnd();
            if (!git.WaitForExit(120_000)) { git.Kill(); return null; }
            return output.Split('\n').Select(l => l.Trim())
                .FirstOrDefault(l => l.StartsWith("password=", StringComparison.Ordinal))?["password=".Length..];
        }
        catch (Exception)
        {
            return null; // git not installed or no stored login
        }
    }

    /// <summary>Minimal GitHub REST client.</summary>
    private sealed class GitHub : IDisposable
    {
        private readonly System.Net.Http.HttpClient _http = new() { BaseAddress = new Uri("https://api.github.com/"), Timeout = TimeSpan.FromSeconds(60) };

        public GitHub(string token)
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BeltAndBarricade-IssueReporter", "1.0"));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        public async Task<JsonNode?> Send(HttpMethod method, string path, JsonNode? body = null, bool allowNotFound = false)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body != null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request);
            string text = await response.Content.ReadAsStringAsync();
            if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                string message = TryParse(text)?["message"]?.GetValue<string>() ?? text;
                string hint = (int)response.StatusCode is 401 or 403 or 404
                    ? " (check that your GitHub login has write access to " + Repo + ")"
                    : "";
                throw new HttpRequestException($"GitHub {(int)response.StatusCode}: {message}{hint}");
            }
            return TryParse(text);
        }

        private static JsonNode? TryParse(string text)
        {
            try { return JsonNode.Parse(text); }
            catch (System.Text.Json.JsonException) { return null; }
        }

        public void Dispose() => _http.Dispose();
    }
}
