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

    /// <summary>
    /// Height is set from the display, not here. Forty logical pixels is a hair over what a
    /// button needs at 100%, and scaling that button without scaling the panel leaves an AutoSize
    /// row docked inside something too short for it, which is a layout fight rather than a clipped
    /// button.
    /// </summary>
    readonly Panel footer = new()
    {
        Dock = DockStyle.Bottom,
        Padding = new(6, 4, 6, 6),
        BackColor = Palette.Background
    };

    /// <summary>
    /// Shared, because a Form does not own the icon it is given and a window can be opened and
    /// hidden many times over one process.
    /// </summary>
    static readonly Icon? icon = EmbeddedIcon.Load();

    /// <summary>
    /// The context menu as a real popup rather than pixels in the canvas, so it gets the OS's
    /// keyboard handling, its screen reader support and its flipping at the screen edge.
    /// <see cref="Screen.Menu" /> stays the one source of truth: this only opens and closes to
    /// agree with it.
    /// </summary>
    readonly ContextMenuStrip contextMenu = ViewerMenu.Create();

    /// <summary>What the popup is currently showing, compared structurally rather than by
    /// reference, and where the right click that asked for it landed.</summary>
    MenuOverlay? shownMenu;

    Point? menuPoint;

    Screen? last;
    CommandKind key;
    int clickedButton = -1;
    int clickedQueueItem = -1;
    int rightClickedQueueItem = -1;
    int clickedMenuItem = -1;
    bool menuClosed;
    int scrollDelta;
    bool closeRequested;
    bool closingForReal;

    public ViewerForm(string title, int width, int height)
    {
        Text = title;
        if (icon is not null)
        {
            Icon = icon;
        }

        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        ClientSize = new(width, height);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        footer.Controls.Add(status);
        footer.Controls.Add(buttonRow);

        // Everything lives in one filling panel so a capture can take the client area alone. Going
        // through the form would include the title bar, which is themed by the OS and would make a
        // committed baseline a picture of the machine that produced it.
        Surface.Controls.Add(canvas);
        Surface.Controls.Add(footer);
        Controls.Add(Surface);

        canvas.QueueItemClicked += _ => clickedQueueItem = _;
        canvas.QueueItemRightClicked += (row, point) =>
        {
            rightClickedQueueItem = row;
            menuPoint = point;
        };
        canvas.Scrolled += _ => scrollDelta += _;

        contextMenu.Closed += (_, e) =>
        {
            shownMenu = null;
            // A chosen item is already reported by its own Click, and the model has to keep the
            // menu open long enough for that index to be resolved against it. Every other reason —
            // Escape, a click outside, losing focus, and this class closing it to match a screen
            // that no longer has a menu — means the model and the popup have drifted apart, and
            // this is the only thing that brings them back.
            if (e.CloseReason != ToolStripDropDownCloseReason.ItemClicked)
            {
                menuClosed = true;
            }
        };
    }

    /// <summary>
    /// Once the handle exists, because DeviceDpi is only meaningful then, and again whenever the
    /// window moves to a display with different scaling.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ScaleFooter();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ScaleFooter();
    }

    void ScaleFooter() =>
        footer.Height = LogicalToDeviceUnits(40);

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
        ApplyMenu(screen);
    }

    void ApplyMenu(Screen screen)
    {
        // No point means no right click has happened, which is also the state a capture runs in:
        // FormsViewerWindow parks the form off screen and calls Apply, and without this guard a
        // captured screen carrying a menu would leave a real popup out there for the rest of the
        // run.
        if (screen.Menu is not { Labels.Count: > 0 } menu ||
            menuPoint is not { } point)
        {
            if (shownMenu is not null)
            {
                shownMenu = null;
                contextMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
            }

            return;
        }

        // Structurally, not by reference: ScreenBuilder rebuilds the label list every frame, so
        // record equality would come back false and re-show the popup on every frame in which
        // anything else changed.
        if (Same(shownMenu, menu))
        {
            return;
        }

        shownMenu = menu;
        ViewerMenu.Fill(contextMenu, menu, _ => clickedMenuItem = _);
        contextMenu.Show(canvas, point);
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
                // Standard rather than System: WinForms draws these itself, including in dark
                // mode, so their pixels are pinned to the .NET version rather than to whatever
                // the OS build's theme renderer does with a Win32 button.
                FlatStyle = FlatStyle.Standard
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
            Rows: canvas.BodyCapacity + ScreenBuilder.Chrome,
            RightClickedQueueItem: rightClickedQueueItem,
            ClickedMenuItem: clickedMenuItem,
            MenuClosed: menuClosed);

        key = CommandKind.None;
        clickedButton = -1;
        clickedQueueItem = -1;
        rightClickedQueueItem = -1;
        clickedMenuItem = -1;
        menuClosed = false;
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
        // With the popup open the keyboard belongs to it: Escape dismisses the menu and the arrows
        // walk it. Mapping them here would quit the viewer with a menu on screen, because Escape
        // is mapped to Quit and swallowed.
        if (contextMenu.Visible)
        {
            return base.ProcessCmdKey(ref message, keyData);
        }

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
            Keys.V => CommandKind.NextVariant,
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
        Same(left.Menu, right.Menu) &&
        Same(left.Left, right.Left) &&
        Same(left.Right, right.Right);

    /// <summary>
    /// A field initializer rather than a component, so it is not in the container Dispose walks.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            contextMenu.Dispose();
        }

        base.Dispose(disposing);
    }

    static bool Same(MenuOverlay? left, MenuOverlay? right) =>
        left is null
            ? right is null
            : right is not null &&
              left.Row == right.Row &&
              left.Labels.SequenceEqual(right.Labels);

    static bool Same(Pane left, Pane right) =>
        left.Header == right.Header &&
        left.ScrollTop == right.ScrollTop &&
        left.TotalRows == right.TotalRows &&
        left.Rows.SequenceEqual(right.Rows);
}
