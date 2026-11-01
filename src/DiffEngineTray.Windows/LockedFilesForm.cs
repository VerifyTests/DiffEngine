class LockedFilesForm :
    Form
{
    public LockedFilesResponse Response { get; private set; } = LockedFilesResponse.Ignore;
    public bool AlwaysKill { get; private set; }

    public LockedFilesForm(TrackedMove move, LockedFiles locked)
    {
        var processNames = locked.ProcessNames;

        Text = $"Locked files: {move.Name}";
        Icon = Images.Active;
        AutoSize = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        Padding = new(8);
        // GrowAndShrink so the form hugs its content height; MinimumSize keeps
        // the width stable. GrowOnly (the AutoSize default) plus a fixed
        // ClientSize left extra padding below the content.
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new(576, 0);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            Dock = DockStyle.Top,
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
            button.Click += (_, _) =>
            {
                clicked();
                DialogResult = DialogResult.OK;
            };
            buttons.Controls.Add(button);
            return button;
        }

        var ignore = AddButton(
            "Ignore",
            () => Response = LockedFilesResponse.Ignore);
        AddButton(
            $"Kill {processNames} and accept",
            () => Response = LockedFilesResponse.Kill);
        AddButton(
            "Kill and accept all pending",
            () => Response = LockedFilesResponse.KillAndAcceptAllPending);
        AddButton(
            "Always kill",
            () =>
            {
                AlwaysKill = true;
                Response = LockedFilesResponse.Kill;
            });

        // One Label per file rather than a single multi-line Label: multi-line
        // Labels round each line's leading whitespace independently, so bullets
        // that share an identical prefix can end up misaligned.
        var filesPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
            Dock = DockStyle.Top,
            Padding = new(12, 0, 3, 3)
        };
        foreach (var file in locked.Files)
        {
            filesPanel.Controls.Add(
                new Label
                {
                    Text = $"• {Path.GetFileName(file)}",
                    AutoSize = true,
                    // GDI rendering measures whitespace consistently; the GDI+
                    // default does not, which is what skews the bullet spacing.
                    UseCompatibleTextRendering = false
                });
        }

        // Dock = Top stacks in reverse add order, so add bottom-most first
        Controls.Add(
            new Label
            {
                Text = "\"Always kill\" is stored in settings and can be changed in Options.",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new(3),
                ForeColor = SystemColors.GrayText
            });
        Controls.Add(buttons);
        Controls.Add(filesPanel);
        Controls.Add(
            new Label
            {
                Text = $"The following files are locked by {processNames}:",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new(3)
            });

        CancelButton = ignore;
    }
}