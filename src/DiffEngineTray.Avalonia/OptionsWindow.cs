using Avalonia.Layout;
using Avalonia.Media;

namespace DiffEngineTray;

// Avalonia equivalent of the Windows Forms OptionsForm.
class OptionsWindow :
    Window
{
    static OptionsWindow? instance;

    readonly HotKeyEditor acceptAll = new();
    readonly HotKeyEditor acceptOpen = new();
    readonly HotKeyEditor discardAll = new();
    readonly CheckBox startup = new() { Content = "Run at login" };
    readonly CheckBox targetOnLeft = new() { Content = "Place target file on the left" };
    readonly NumericUpDown maxInstances = new() { Minimum = 1, Maximum = 100, Increment = 1, Width = 120 };
    readonly TextBlock errors = new() { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap };
    readonly Func<Settings, Task<IReadOnlyCollection<string>>> trySave;
    readonly string? version;

    public OptionsWindow(Settings settings, Func<Settings, Task<IReadOnlyCollection<string>>> trySave, string? version = null)
    {
        this.trySave = trySave;
        this.version = version;

        Title = "DiffEngineTray Options";
        Icon = AppIcons.Active;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        acceptAll.Value = settings.AcceptAllHotKey;
        acceptOpen.Value = settings.AcceptOpenHotKey;
        discardAll.Value = settings.DiscardAllHotKey;
        startup.IsChecked = settings.RunAtStartup;
        targetOnLeft.IsChecked = settings.TargetOnLeft;
        maxInstances.Value = settings.MaxInstancesToLaunch;

        Content = BuildLayout();
    }

    public static void Launch(Settings settings, Func<Settings, Task<IReadOnlyCollection<string>>> trySave)
    {
        if (instance != null)
        {
            instance.Activate();
            return;
        }

        var window = new OptionsWindow(settings, trySave);
        instance = window;
        window.Closed += (_, _) => instance = null;
        window.Show();
        window.Activate();
    }

    Control BuildLayout()
    {
        var save = new Button { Content = "Save", MinWidth = 80, IsDefault = true };
        save.Click += (_, _) => Save();
        var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
        cancel.Click += (_, _) => Close();

        var panel = new StackPanel
        {
            Margin = new(20),
            Spacing = 12,
            MinWidth = 360,
            Children =
            {
                Header("Hotkeys"),
                LabeledRow("Accept all", acceptAll),
                LabeledRow("Accept open", acceptOpen),
                LabeledRow("Discard all", discardAll),
                Header("Behaviour"),
                startup,
                targetOnLeft,
                LabeledRow("Max diff tool instances", maxInstances),
                Header("About"),
                new TextBlock { Text = $"Version: {version ?? VersionReader.VersionString}" },
                LinkButton("DiffEngine on GitHub", "https://github.com/VerifyTests/DiffEngine"),
                LinkButton("Tray documentation", "https://github.com/VerifyTests/DiffEngine/blob/master/docs/tray.md"),
                UpdateButton(),
                errors,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 10,
                    Children = { save, cancel }
                }
            }
        };
        return panel;
    }

    static TextBlock Header(string text) =>
        new()
        {
            Text = text,
            FontWeight = FontWeight.Bold,
            Margin = new(0, 8, 0, 0)
        };

    static Control LabeledRow(string label, Control editor) =>
        new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    Width = 150,
                    VerticalAlignment = VerticalAlignment.Center
                },
                editor
            }
        };

    static Button LinkButton(string text, string url)
    {
        var button = new Button { Content = text };
        button.Click += (_, _) => LinkLauncher.LaunchUrl(url);
        return button;
    }

    static Button UpdateButton()
    {
        var button = new Button { Content = "Update DiffEngineTray" };
        button.Click += (_, _) => AvaloniaUpdater.Run();
        return button;
    }

    async void Save()
    {
        var newSettings = new Settings
        {
            AcceptAllHotKey = acceptAll.Value,
            AcceptOpenHotKey = acceptOpen.Value,
            DiscardAllHotKey = discardAll.Value,
            RunAtStartup = startup.IsChecked == true,
            TargetOnLeft = targetOnLeft.IsChecked == true,
            MaxInstancesToLaunch = (int) (maxInstances.Value ?? 1)
        };

        var saveErrors = (await trySave(newSettings)).ToList();
        if (saveErrors.Count == 0)
        {
            Close();
            return;
        }

        errors.Text = string.Join(Environment.NewLine, saveErrors.Select(_ => $" * {_}"));
    }
}

// Modifier checkboxes + a key text box, mapping to/from the shared HotKey model.
class HotKeyEditor :
    StackPanel
{
    readonly CheckBox control = new() { Content = "Ctrl" };
    readonly CheckBox alt = new() { Content = "Alt" };
    readonly CheckBox shift = new() { Content = "Shift" };
    readonly TextBox key = new() { Width = 70, Watermark = "Key" };

    public HotKeyEditor()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        Children.Add(control);
        Children.Add(alt);
        Children.Add(shift);
        Children.Add(key);
    }

    public HotKey? Value
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key.Text))
            {
                return null;
            }

            return new()
            {
                Control = control.IsChecked == true,
                Alt = alt.IsChecked == true,
                Shift = shift.IsChecked == true,
                Key = key.Text.Trim()
            };
        }
        set
        {
            control.IsChecked = value?.Control ?? false;
            alt.IsChecked = value?.Alt ?? false;
            shift.IsChecked = value?.Shift ?? false;
            key.Text = value?.Key ?? "";
        }
    }
}
