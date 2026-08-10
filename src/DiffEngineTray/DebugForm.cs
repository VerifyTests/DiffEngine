/// <summary>
/// The "Debug view" window: <see cref="DebugReport"/> in a box that can be re-read and copied.
/// <para>
/// Built from a callback rather than from the Tracker, so Refresh re-reads rather than re-opens,
/// and so the window can be rendered over a fixed report in a test.
/// </para>
/// </summary>
class DebugForm :
    Form
{
    TextBox content;
    Func<string> build;

    public DebugForm(Func<string> build)
    {
        this.build = build;

        Text = "DiffEngineTray debug";
        Icon = Images.Active;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        ClientSize = new(900, 620);
        MinimumSize = new(500, 300);
        Padding = new(8);

        content = new()
        {
            Multiline = true,
            ReadOnly = true,
            // The report lines up its fields in a column, and wrapping a path would break that.
            WordWrap = false,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new("Consolas", 9F),
            // A read only TextBox defaults to the Control grey, which reads as disabled.
            BackColor = SystemColors.Window
        };

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Bottom,
            Padding = new(3)
        };

        Button AddButton(string text, Action clicked)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                UseVisualStyleBackColor = true
            };
            button.Click += (_, _) => clicked();
            buttons.Controls.Add(button);
            return button;
        }

        var close = AddButton("Close", Close);
        AddButton("Copy", Copy);
        AddButton("Refresh", Reload);

        // Dock = Fill is added first so the docked buttons take their strip from it, rather than
        // covering it
        Controls.Add(content);
        Controls.Add(buttons);

        CancelButton = close;
        Reload();
    }

    /// <summary>
    /// Nothing pushes at this window. Snapshots arrive on a socket thread and moves are reconciled
    /// by a timer, so what is shown is the moment it was read, and this takes a newer one.
    /// </summary>
    void Reload()
    {
        try
        {
            content.Text = build();
        }
        catch (Exception exception)
        {
            // This runs from a click, so throwing would take the tray with it. A view for looking
            // at what went wrong can show that too, in the box that was going to hold the report.
            Log.Error(exception, "Failed to build the debug report");
            content.Text = exception.ToString();
        }
    }

    void Copy()
    {
        if (content.TextLength == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(content.Text);
        }
        catch (ExternalException exception)
        {
            // Another process can have the clipboard open. Failing to copy a debug report is not
            // worth taking the tray down for.
            ExceptionHandler.Handle("Failed to copy the debug report to the clipboard", exception);
        }
    }
}
