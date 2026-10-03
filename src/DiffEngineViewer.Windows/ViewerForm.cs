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
        Name = "status",
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        ForeColor = Palette.Dim,
        AutoSize = false,
        // The status line is built from paths, solution names and whatever the applier said, and a
        // Label reads an ampersand in any of those as a mnemonic: "R&D" drew as "R_D" with D live
        // as an accelerator.
        UseMnemonic = false
    };

    readonly List<FormsButton> pool = [];

    /// <summary>
    /// One bar, not two: both panes are sliced from the same ScrollTop, so they scroll together.
    /// Docked on the surface rather than parented to the canvas, which leaves every one of the
    /// canvas's own measurements alone — it is simply a little narrower.
    /// </summary>
    readonly VScrollBar scrollBar = new()
    {
        Dock = DockStyle.Right,
        Minimum = 0,
        SmallChange = 1,
        // Tab is mapped to the next queue item, so focus never reaches this anyway, and a focused
        // scrollbar eating the arrow keys would be two scroll models fighting.
        TabStop = false
    };

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

    /// <summary>
    /// Keys, clicks and menu events, in the order they happened, one handed over per frame.
    /// <para>
    /// A slot per kind used to hold them: two presses of Down between frames scrolled once, and a
    /// key then a click were applied click first. That is not an edge case when a frame is slow - the
    /// loop waiting behind an accept on InlineApplier's mutex - and d pressed on the entry being
    /// read and a click on another row then discarded the clicked one, which the reader had never
    /// looked at. With a, it would have been accepted into source.
    /// </para>
    /// </summary>
    readonly Queue<Discrete> discrete = new();

    readonly record struct Discrete(
        CommandKind Key = CommandKind.None,
        int Button = -1,
        int QueueItem = -1,
        int RightClickedQueueItem = -1,
        int MenuItem = -1,
        bool MenuClosed = false,
        int RightClickedPane = -1);

    static readonly Discrete nothing = new(Key: CommandKind.None);

    /// <summary>
    /// Whether input is waiting for a frame, so the loop takes the next frame now rather than
    /// sleeping until one is due.
    /// </summary>
    public bool Pending => discrete.Count > 0;

    int scrollTo = -1;
    int scrollDelta;
    int zoomDelta;
    bool closeRequested;
    bool closingForReal;

    public ViewerForm(string title, int width, int height, WindowPlacement? placement = null)
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
        if (placement is { } saved &&
            Restorable(saved, WorkingAreas()) is { } bounds)
        {
            // Already in device pixels, on the display they were measured on, so not scaled again
            sized = true;
            restored = bounds;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            // Before the window is shown, so it comes up maximised rather than growing into it,
            // on the display its bounds are on. Those stay what it restores to.
            if (saved.Maximized)
            {
                maximized = true;
                WindowState = FormWindowState.Maximized;
            }
        }

        KeyPreview = true;

        footer.Controls.Add(status);
        footer.Controls.Add(buttonRow);

        // Everything lives in one filling panel so a capture can take the client area alone. Going
        // through the form would include the title bar, which is themed by the OS and would make a
        // committed baseline a picture of the machine that produced it.
        // Docking is resolved in reverse: the footer takes the bottom, then the bar takes the right
        // of what is left, and the canvas fills the rest.
        Surface.Controls.Add(canvas);
        Surface.Controls.Add(scrollBar);
        Surface.Controls.Add(footer);
        Controls.Add(Surface);

        // Scroll rather than ValueChanged, which also fires for this class's own model driven
        // assignment and would turn every wheel notch into a round trip fighting the clamp.
        scrollBar.Scroll += (_, e) =>
        {
            scrollTo = e.NewValue;
            // Every part of the bar is tracked in its own modal loop, from the press until the
            // release: an arrow or the trough held down as much as the thumb dragged, which was
            // the only one this entered for, so holding an arrow moved nothing until it was let
            // go. EndScroll is what the bar sends as that loop ends, whichever part it was, and
            // ThumbPosition comes just ahead of it.
            if (e.Type is ScrollEventType.ThumbPosition or ScrollEventType.EndScroll)
            {
                ExitModal();
                return;
            }

            EnterModal();
        };
        modalFrames.Tick += (_, _) =>
        {
            if (Frame is { } frame)
            {
                Apply(frame());
                Animate();
            }
        };

        canvas.QueueItemClicked += _ => discrete.Enqueue(new(QueueItem: _));
        canvas.QueueItemRightClicked += (row, point) =>
        {
            discrete.Enqueue(new(RightClickedQueueItem: row));
            menuPoint = point;
        };
        canvas.PaneRightClicked += (side, point) =>
        {
            discrete.Enqueue(new(RightClickedPane: (int) side));
            menuPoint = point;
        };
        canvas.Scrolled += _ => scrollDelta += _;
        canvas.Zoomed += _ => zoomDelta += _;

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
                discrete.Enqueue(new(MenuClosed: true));
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
        if (!sized)
        {
            sized = true;
            var area = System.Windows.Forms.Screen.FromControl(this).WorkingArea;
            ClientSize = InitialClientSize(ClientSize, DeviceDpi, area.Size);

            // Centred again, for the size it has now. WinForms centres a window as it creates it,
            // which is before this, so it was centred for the size asked for in logical pixels and
            // then grew down and to the right from there: at 150% on a 1080p display the footer
            // was under the taskbar, and that was the placement remembered for every run after.
            if (StartPosition == FormStartPosition.CenterScreen &&
                WindowState == FormWindowState.Normal)
            {
                Location = Centred(area, Size);
            }
        }
        else if (restored is { } bounds &&
                 WindowState == FormWindowState.Normal &&
                 Bounds != bounds)
        {
            // Asked for before there was a handle, on a display whose scaling the form had not
            // met yet. Whatever creating it there did to the size, this is the size it had.
            Bounds = bounds;
        }

        restored = null;
        ScaleChrome();
    }

    bool sized;

    /// <summary>
    /// The bounds a remembered placement asked for, until the handle exists and has them.
    /// </summary>
    Rectangle? restored;

    /// <summary>
    /// Whether the window is maximised, or was when it was minimised: a minimised window's own
    /// state says only that it is minimised, and it comes back as whichever it was.
    /// </summary>
    bool maximized;

    /// <summary>
    /// See <see cref="IViewerWindow.Placement"/>. Read from the window while there is one, and
    /// what that last came to once there is not.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WindowPlacement? Placement
    {
        get
        {
            if (IsHandleCreated &&
                !IsDisposed)
            {
                // Maximised or minimised, the bounds are of neither: RestoreBounds is where the
                // window goes back to, which is the only size worth opening the next one at.
                var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (bounds is {Width: > 0, Height: > 0})
                {
                    field = new(bounds.X, bounds.Y, bounds.Width, bounds.Height, maximized);
                }
            }

            return field;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState != FormWindowState.Minimized)
        {
            maximized = WindowState == FormWindowState.Maximized;
        }
    }

    static List<Rectangle> WorkingAreas() =>
        System.Windows.Forms.Screen.AllScreens
            .Select(_ => _.WorkingArea)
            .ToList();

    /// <summary>
    /// Where a remembered window may open, or null when it should open as a new one does.
    /// <para>
    /// The displays are not the ones it was remembered on often enough to matter: a laptop taken
    /// off its dock remembers a window on a monitor that is no longer there, and opening it there
    /// is a viewer that looks as if it did not start. So it opens where it was only when most of
    /// it, and its title bar, are on a display there is now; otherwise it is moved onto the one
    /// it overlaps most, and with none it is not used at all.
    /// </para>
    /// <para>
    /// Left exactly where it was whenever it can be, rather than tidied into the working area. A
    /// window snapped to an edge sits a few pixels past it, the width of a resize border nobody
    /// can see, and moving that inside would walk it across the screen a border at a time.
    /// </para>
    /// </summary>
    internal static Rectangle? Restorable(WindowPlacement saved, IReadOnlyList<Rectangle> workingAreas)
    {
        var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
        // Smaller than anything a person could have left it, so not something a window reported
        if (bounds.Width < minimumRestored.Width ||
            bounds.Height < minimumRestored.Height)
        {
            return null;
        }

        Rectangle? best = null;
        long overlap = 0;
        foreach (var area in workingAreas)
        {
            var shared = Rectangle.Intersect(area, bounds);
            var size = (long) shared.Width * shared.Height;
            if (size > overlap)
            {
                overlap = size;
                best = area;
            }
        }

        if (best is not { } screen)
        {
            return null;
        }

        if (overlap * 2 >= (long) bounds.Width * bounds.Height &&
            bounds.Top >= screen.Top - edgeSlack &&
            bounds.Top < screen.Bottom - edgeSlack)
        {
            return bounds;
        }

        var width = Math.Min(bounds.Width, screen.Width);
        var height = Math.Min(bounds.Height, screen.Height);
        return new(
            Math.Clamp(bounds.X, screen.Left, screen.Right - width),
            Math.Clamp(bounds.Y, screen.Top, screen.Bottom - height),
            width,
            height);
    }

    static readonly Size minimumRestored = new(200, 150);

    /// <summary>
    /// How far past the top of a display a title bar may be and still be reachable: more than the
    /// invisible border a snapped window hangs over by, less than the bar itself.
    /// </summary>
    const int edgeSlack = 16;

    /// <summary>
    /// The size asked for is in logical pixels, and the window is per monitor aware, so it is
    /// scaled to the display it opens on - once, before it is shown, and centred again for the
    /// size that comes to, since WinForms had already centred it for the other. Unscaled, it was
    /// 1100 by 700 device pixels while the text grew with the display: at 200% each pane had room
    /// for four characters. Moving to another display afterwards is Windows' to scale.
    /// <para>
    /// Kept inside the working area, which a scaled window can outgrow: 1100 by 700 at 150% is too
    /// tall for a 1080p screen once the taskbar is taken off.
    /// </para>
    /// </summary>
    internal static Size InitialClientSize(Size logical, int dpi, Size workingArea)
    {
        var width = logical.Width * dpi / 96;
        var height = logical.Height * dpi / 96;
        // Room for the frame and title bar, which are outside the client area
        var maxWidth = workingArea.Width * 9 / 10;
        var maxHeight = workingArea.Height * 9 / 10;
        return new(Math.Min(width, maxWidth), Math.Min(height, maxHeight));
    }

    /// <summary>
    /// Where a window of <paramref name="size"/> sits to be in the middle of
    /// <paramref name="area"/>. One too large for it starts at the area's top left, so its title
    /// bar is still in reach.
    /// </summary>
    internal static Point Centred(Rectangle area, Size size) =>
        new(
            Math.Max(area.X, area.X + (area.Width - size.Width) / 2),
            Math.Max(area.Y, area.Y + (area.Height - size.Height) / 2));

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ScaleChrome();
    }

    /// <summary>
    /// One frame of the loop, run on <see cref="modalFrames"/> while user32 holds the thread in a
    /// modal loop of its own. See <see cref="ILoopHooks.Frame"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Func<Screen>? Frame { get; set; }

    /// <summary>
    /// See <see cref="ILoopHooks.SessionEnding"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Action? SessionEnding { get; set; }

    /// <summary>
    /// A WinForms timer because its ticks are window messages, and a modal loop - the scroll bar's
    /// while the thumb is dragged, the frame's while the window is moved or sized - still
    /// dispatches those, where it never returns to the loop that called DoEvents.
    /// </summary>
    readonly System.Windows.Forms.Timer modalFrames = new()
    {
        Interval = 16
    };

    void EnterModal()
    {
        if (Frame is not null &&
            !modalFrames.Enabled)
        {
            modalFrames.Start();
        }
    }

    void ExitModal() =>
        modalFrames.Stop();

    /// <summary>
    /// The loop is presenting a frame of its own, so nothing is holding the thread: whatever modal
    /// loop was entered has returned. Its exit is normally what says so. A scroll sent to the bar
    /// by something other than a press - an accessibility tool, say - need not be followed by an
    /// EndScroll, and frames would then come from the timer as well as the loop from there on.
    /// </summary>
    public void LoopReturned() =>
        ExitModal();

    const int enterSizeMove = 0x0231;
    const int exitSizeMove = 0x0232;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == enterSizeMove)
        {
            EnterModal();
        }
        else if (message.Msg == exitSizeMove)
        {
            ExitModal();
        }

        base.WndProc(ref message);
    }

    /// <summary>
    /// Persisted here, synchronously, when the session is ending. WinForms closes the form inside
    /// WM_ENDSESSION, and Windows may end the process as soon as that returns - before the loop has
    /// even noticed the form is gone, let alone got through its own shutdown to the staging.
    /// </summary>
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (EndsTheSession(e.CloseReason))
        {
            SessionEnding?.Invoke();
        }

        base.OnFormClosed(e);
    }

    void ScaleChrome()
    {
        footer.Height = LogicalToDeviceUnits(40);
        scrollBar.Width = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
    }

    /// <summary>
    /// Brings the window up, for a snapshot that has just arrived and wants reading.
    /// <para>
    /// The restore is the part that was missing. BringToFront and Activate leave a minimised
    /// window minimised - the taskbar button flashes and nothing else happens - so a viewer that
    /// had been minimised never showed the snapshot it was being asked to show, and the queue
    /// filled up out of sight.
    /// </para>
    /// </summary>
    public void Raise()
    {
        Visible = true;
        if (WindowState == FormWindowState.Minimized)
        {
            // Back to what it was minimised from, as the taskbar would put it. Always to normal,
            // a window the reader had maximised came back at its restored size, and was then
            // remembered as one they had not maximised.
            WindowState = maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }

        BringToFront();
        Activate();
    }

    public void Apply(Screen screen)
    {
        // The loop hands over the screen it handed over last frame for as long as nothing has
        // happened (ScreenCache), which is nearly every frame, and those stop here.
        if (ReferenceEquals(last, screen))
        {
            return;
        }

        // A new screen need not be a different one: a state can change in a way that does not
        // show, and record equality stops at the lists, which compare by reference. Without this
        // such a frame repainted the whole window. Kept as the last one either way, so the frames
        // after it stop at the reference above.
        if (Same(last, screen))
        {
            last = screen;
            return;
        }

        last = screen;
        status.Text = screen.Status;
        ApplyButtons(screen);
        canvas.Draw(screen);
        ApplyScroll(screen);
        ApplyMenu(screen);
    }

    /// <summary>
    /// See <see cref="ViewerCanvas.Synchronous" />.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Synchronous
    {
        get => canvas.Synchronous;
        set => canvas.Synchronous = value;
    }

    /// <summary>
    /// See <see cref="ViewerCanvas.Animate" />.
    /// </summary>
    public void Animate() =>
        canvas.Animate();

    /// <summary>
    /// The bar follows the model rather than owning the position, so it agrees with the keyboard
    /// and the wheel. The left pane, because that is the row count the session clamps against.
    /// <para>
    /// Visible is the slice the model cut, as the macOS and Linux bars read it, rather than the
    /// body: a document with its page under its text shows half the rows the body has room for.
    /// A slice shorter than the body only ever means everything fits, which needs no bar either way.
    /// </para>
    /// </summary>
    void ApplyScroll(Screen screen)
    {
        var visible = Math.Max(1, Math.Min(screen.Rows - ScreenBuilder.Chrome, screen.Left.Rows.Count));
        var range = PaneScroll.For(screen.Left.TotalRows, visible, screen.Left.ScrollTop);
        // Maximum first: lowering it clamps Value, and LargeChange is itself clamped to the range.
        scrollBar.Maximum = range.Maximum;
        scrollBar.LargeChange = range.LargeChange;
        if (scrollBar.Value != range.Value)
        {
            // Only when it actually disagrees, so dragging the thumb is not fought by an
            // assignment on every frame of the drag.
            scrollBar.Value = range.Value;
        }
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
        ViewerMenu.Fill(contextMenu, menu, _ => discrete.Enqueue(new(MenuItem: _)));
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
                // Sized to the label it has now. The pool relabels its buttons as the screen
                // changes, and a button's default, GrowOnly, kept each one as wide as the longest
                // label it had ever held: the footer's layout was the history of the session, and
                // the pixel baselines were the history of the test run.
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                // The size a button has by default, which GrowOnly never went under, so a short
                // label does not make a button smaller than it always was
                MinimumSize = LogicalToDeviceUnits(new Size(75, 23)),
                Margin = new(0, 0, 6, 0),
                // Standard rather than System: WinForms draws these itself, including in dark
                // mode, so their pixels are pinned to the .NET version rather than to whatever
                // the OS build's theme renderer does with a Win32 button.
                FlatStyle = FlatStyle.Standard
            };
            button.Click += (_, _) => discrete.Enqueue(new(Button: index));
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
        var drag = canvas.TakeDrag();
        var pan = canvas.TakePan();
        // Not default: that zeroes every index, and zero is the first button and the first row
        if (!discrete.TryDequeue(out var next))
        {
            next = nothing;
        }

        var input = new ViewerInput(
            Key: next.Key,
            ClickedButton: next.Button,
            ClickedQueueItem: next.QueueItem,
            ScrollDelta: scrollDelta,
            CloseRequested: closeRequested,
            Columns: canvas.ColumnCapacity,
            // ScreenBuilder subtracts Chrome to get the body, so adding it back asks for exactly
            // the rows the canvas can draw rather than a guess from a fixed cell height.
            Rows: canvas.BodyCapacity + ScreenBuilder.Chrome,
            RightClickedQueueItem: next.RightClickedQueueItem,
            ClickedMenuItem: next.MenuItem,
            MenuClosed: next.MenuClosed,
            ScrollTo: scrollTo,
            DragSide: drag is null ? -1 : (int) drag.Value.Side,
            DragAnchorRow: drag?.AnchorRow ?? 0,
            DragAnchorColumn: drag?.AnchorColumn ?? 0,
            DragFocusRow: drag?.FocusRow ?? 0,
            DragFocusColumn: drag?.FocusColumn ?? 0,
            ZoomDelta: zoomDelta,
            PanX: pan?.X ?? -1,
            PanY: pan?.Y ?? -1,
            RightClickedPane: next.RightClickedPane);

        scrollTo = -1;
        scrollDelta = 0;
        zoomDelta = 0;
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
        // Cancelled for a close the user asked for, because whether that means hide or exit is
        // ViewerProgram's rule and it needs a tray check to decide. CloseForReal is how the answer
        // comes back.
        //
        // Never for a close the session is ending: WinForms answers WM_QUERYENDSESSION with
        // !e.Cancel, so refusing made Windows report the viewer as preventing shutdown, and with a
        // tray running the loop only hid the window - leaving the process blocking until the user
        // chose "Shut down anyway". Letting it through is safe because the loop watches for a
        // disposed form and returns, which runs the same shutdown it would have run anyway.
        if (!closingForReal && !EndsTheSession(e.CloseReason))
        {
            closeRequested = true;
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// The process is going away whatever this form says. Task Manager's End Task is here with
    /// shutdown because refusing it buys the same nothing: the user has already decided.
    /// </summary>
    internal static bool EndsTheSession(CloseReason reason) =>
        reason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing;

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

        discrete.Enqueue(new(Key: command));
        return true;
    }

    internal static CommandKind Map(Keys keyData)
    {
        // No command is an Alt chord, and the switch below reads only the key code, so Alt+A was
        // accept, Alt+D discard and Alt+Q quit: a reach for a menu that is not there wrote a
        // snapshot into source. Ahead of Control, because Alt Gr arrives as both, and a character
        // typed with it is not a Control chord either.
        if ((keyData & Keys.Alt) == Keys.Alt)
        {
            return CommandKind.None;
        }

        var shift = (keyData & Keys.Shift) == Keys.Shift;
        var code = keyData & Keys.KeyCode;
        // Answered on its own rather than folded into the switch, which reads only the key code:
        // ctrl+a is select all, and without this it was accept - the modifier the whole point of
        // the chord went straight through.
        if ((keyData & Keys.Control) == Keys.Control)
        {
            return code switch
            {
                // Insert as well as C: the older chord, and still the one some hands reach for
                Keys.C or Keys.Insert => CommandKind.Copy,
                Keys.A => CommandKind.SelectAll,
                // With control as well as without, since that is the chord everything else that
                // zooms taught
                _ => Zoom(code)
            };
        }

        if (Zoom(code) is var zoom and not CommandKind.None)
        {
            return zoom;
        }

        return code switch
        {
            Keys.Up => CommandKind.ScrollUp,
            Keys.Down => CommandKind.ScrollDown,
            Keys.PageUp => CommandKind.PageUp,
            Keys.PageDown => CommandKind.PageDown,
            Keys.Home => CommandKind.ScrollHome,
            Keys.End => CommandKind.ScrollEnd,
            Keys.N => CommandKind.NextChange,
            Keys.P => CommandKind.PreviousChange,
            Keys.M => CommandKind.ToggleMinimal,
            Keys.R => CommandKind.ToggleDrawing,
            Keys.J => CommandKind.NextProjection,
            Keys.OemOpenBrackets => CommandKind.PreviousPage,
            Keys.OemCloseBrackets => CommandKind.NextPage,
            Keys.Tab => shift ? CommandKind.PreviousItem : CommandKind.NextItem,
            Keys.A => shift ? CommandKind.AcceptAll : CommandKind.Accept,
            Keys.D => CommandKind.Discard,
            Keys.V => CommandKind.NextVariant,
            Keys.Q or Keys.Escape => CommandKind.Quit,
            _ => CommandKind.None
        };
    }

    /// <summary>
    /// Plus, minus and zero, on the main keys and on the number pad. Plus is the equals key
    /// whether or not shift is held: nobody reaches for shift to zoom in.
    /// </summary>
    static CommandKind Zoom(Keys code) =>
        code switch
        {
            Keys.Oemplus or Keys.Add => CommandKind.ZoomIn,
            Keys.OemMinus or Keys.Subtract => CommandKind.ZoomOut,
            Keys.D0 or Keys.NumPad0 => CommandKind.ZoomReset,
            _ => CommandKind.None
        };

    /// <summary>
    /// Records all the way down, so this is structural apart from the lists, which compare by
    /// reference and are rebuilt every frame.
    /// </summary>
    static bool Same(Screen? left, Screen right) =>
        left is not null &&
        // The grid, because a resize that changes nothing else still changes how much of a pane
        // fits, which is the scrollbar's LargeChange.
        left.Columns == right.Columns &&
        left.Rows == right.Rows &&
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
            modalFrames.Dispose();
        }

        base.Dispose(disposing);
    }

    static bool Same(MenuOverlay? left, MenuOverlay? right) =>
        left is null
            ? right is null
            : right is not null &&
              left.Row == right.Row &&
              left.Pane == right.Pane &&
              left.Labels.SequenceEqual(right.Labels);

    internal static bool Same(Pane left, Pane right) =>
        left.Header == right.Header &&
        left.ScrollTop == right.ScrollTop &&
        left.TotalRows == right.TotalRows &&
        // Records all the way down, so this compares the path, the size and the content stamp. A
        // re-run that rewrites a received image at the same size changes nothing else about the
        // screen - the rows say format, dimensions and byte count, and for BMP those hold - so
        // without it Apply returned before repainting and the pane kept the previous picture.
        left.Image == right.Image &&
        left.Rows.SequenceEqual(right.Rows);
}
