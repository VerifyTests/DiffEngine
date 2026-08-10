/// <summary>
/// The window. Accumulates what the user did so <see cref="IViewerWindow.Poll" /> can drain it,
/// which keeps the loop in ViewerProgram identical to the one the native heads run.
/// </summary>
[DesignerCategory("")]
sealed class ViewerForm : Form
{
    readonly ViewerCanvas canvas = new()
    {
        Dock = DockStyle.Fill
    };

    readonly FlowLayoutPanel buttonRow = new()
    {
        Dock = DockStyle.Left,
        AutoSize = true,
        WrapContents = false,
        Margin = Padding.Empty
    };

    readonly Label status = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Palette.Dim,
        AutoSize = false
    };

    readonly List<FormsButton> pool = [];

    /// <summary>
    /// The client area as one control, so it can be rendered to a bitmap without the window frame.
    /// </summary>
    public Panel Surface { get; } = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Palette.Background
    };

    Screen? last;
    CommandKind key;
    int clickedButton = -1;
    int clickedQueueItem = -1;
    int scrollDelta;
    bool closeRequested;
    bool closingForReal;

    public ViewerForm(string title, int width, int height)
    {
        Text = title;
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        ClientSize = new(width, height);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            Padding = new(6, 4, 6, 6),
            BackColor = Palette.Background
        };
        footer.Controls.Add(status);
        footer.Controls.Add(buttonRow);

        // Everything lives in one filling panel so a capture can take the client area alone. Going
        // through the form would include the title bar, which is themed by the OS and would make a
        // committed baseline a picture of the machine that produced it.
        Surface.Controls.Add(canvas);
        Surface.Controls.Add(footer);
        Controls.Add(Surface);

        canvas.QueueItemClicked += _ => clickedQueueItem = _;
        canvas.Scrolled += _ => scrollDelta += _;
    }

    public void Apply(Screen screen)
    {
        // ScreenBuilder allocates a fresh Screen every frame, so record equality would never hit.
        // Without this the window repaints sixty times a second while sitting idle.
        if (Same(last, screen))
        {
            return;
        }

        last = screen;
        status.Text = screen.Status;
        ApplyButtons(screen);
        canvas.Draw(screen);
    }

    void ApplyButtons(Screen screen)
    {
        while (pool.Count < screen.Buttons.Count)
        {
            var index = pool.Count;
            var button = new FormsButton
            {
                AutoSize = true,
                Margin = new(0, 0, 6, 0),
                FlatStyle = FlatStyle.System
            };
            button.Click += (_, _) => clickedButton = index;
            pool.Add(button);
            buttonRow.Controls.Add(button);
        }

        for (var index = 0; index < pool.Count; index++)
        {
            var button = pool[index];
            if (index >= screen.Buttons.Count)
            {
                button.Visible = false;
                continue;
            }

            var model = screen.Buttons[index];
            button.Text = model.Label;
            button.Enabled = model.Enabled;
            button.Visible = true;
        }
    }

    public ViewerInput Drain()
    {
        var input = new ViewerInput(
            Key: key,
            ClickedButton: clickedButton,
            ClickedQueueItem: clickedQueueItem,
            ScrollDelta: scrollDelta,
            CloseRequested: closeRequested,
            Columns: canvas.ColumnCapacity,
            // ScreenBuilder subtracts Chrome to get the body, so adding it back asks for exactly
            // the rows the canvas can draw rather than a guess from a fixed cell height.
            Rows: canvas.BodyCapacity + ScreenBuilder.Chrome);

        key = CommandKind.None;
        clickedButton = -1;
        clickedQueueItem = -1;
        scrollDelta = 0;
        closeRequested = false;
        return input;
    }

    public void CloseForReal()
    {
        closingForReal = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Always cancelled, because whether closing means hide or exit is ViewerProgram's rule and
        // it needs a tray check to decide. CloseForReal is how the answer comes back.
        if (!closingForReal)
        {
            closeRequested = true;
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// ProcessCmdKey rather than OnKeyDown, because Tab and Escape are consumed by focus
    /// navigation and the default button before a key handler would ever see them.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        var command = Map(keyData);
        if (command == CommandKind.None)
        {
            return base.ProcessCmdKey(ref message, keyData);
        }

        key = command;
        return true;
    }

    static CommandKind Map(Keys keyData)
    {
        var shift = (keyData & Keys.Shift) == Keys.Shift;
        return (keyData & Keys.KeyCode) switch
        {
            Keys.Up => CommandKind.ScrollUp,
            Keys.Down => CommandKind.ScrollDown,
            Keys.PageUp => CommandKind.PageUp,
            Keys.PageDown => CommandKind.PageDown,
            Keys.Home => CommandKind.ScrollHome,
            Keys.End => CommandKind.ScrollEnd,
            Keys.N => CommandKind.NextChange,
            Keys.P => CommandKind.PreviousChange,
            Keys.Tab => shift ? CommandKind.PreviousItem : CommandKind.NextItem,
            Keys.A => shift ? CommandKind.AcceptAll : CommandKind.Accept,
            Keys.D => CommandKind.Discard,
            Keys.Q or Keys.Escape => CommandKind.Quit,
            _ => CommandKind.None
        };
    }

    /// <summary>
    /// Records all the way down, so this is structural apart from the lists, which compare by
    /// reference and are rebuilt every frame.
    /// </summary>
    static bool Same(Screen? left, Screen right) =>
        left is not null &&
        left.Title == right.Title &&
        left.Subtitle == right.Subtitle &&
        left.Status == right.Status &&
        left.Queue.SequenceEqual(right.Queue) &&
        left.Buttons.SequenceEqual(right.Buttons) &&
        Same(left.Left, right.Left) &&
        Same(left.Right, right.Right);

    static bool Same(Pane left, Pane right) =>
        left.Header == right.Header &&
        left.ScrollTop == right.ScrollTop &&
        left.TotalRows == right.TotalRows &&
        left.Rows.SequenceEqual(right.Rows);
}
