using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace DiffEngineTray;

static class Dialogs
{
    // Synchronous confirm used by the core (ExceptionHandler) from background threads.
    public static bool Confirm(string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            // Cannot block the UI thread for a modal result; skip the prompt and just proceed to log.
            return false;
        }

        var completion = new TaskCompletionSource<bool>();
        Dispatcher.UIThread.Post(
            async void () =>
            {
                try
                {
                    completion.SetResult(await ShowConfirm(text));
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            });
        return completion.Task.GetAwaiter().GetResult();
    }

    public static Task<bool> ConfirmAsync(string text) =>
        ShowConfirm(text);

    public static Task Message(string text) =>
        ShowMessage(text);

    public static async Task<string?> PickFolder(string title)
    {
        var window = HiddenWindow();
        window.Show();
        try
        {
            var folders = await window.StorageProvider.OpenFolderPickerAsync(
                new()
                {
                    Title = title,
                    AllowMultiple = false
                });
            if (folders.Count == 0)
            {
                return null;
            }

            return folders[0].Path.LocalPath;
        }
        finally
        {
            window.Close();
        }
    }

    static Task<bool> ShowConfirm(string text)
    {
        var completion = new TaskCompletionSource<bool>();
        var yes = new Button { Content = "Yes", MinWidth = 80, IsDefault = true };
        var no = new Button { Content = "No", MinWidth = 80, IsCancel = true };
        var window = MessageWindow(text, yes, no);
        yes.Click += (_, _) =>
        {
            completion.TrySetResult(true);
            window.Close();
        };
        no.Click += (_, _) =>
        {
            completion.TrySetResult(false);
            window.Close();
        };
        window.Closed += (_, _) => completion.TrySetResult(false);
        window.Show();
        window.Activate();
        return completion.Task;
    }

    static Task ShowMessage(string text)
    {
        var completion = new TaskCompletionSource();
        var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, IsCancel = true };
        var window = MessageWindow(text, ok);
        ok.Click += (_, _) => window.Close();
        window.Closed += (_, _) => completion.TrySetResult();
        window.Show();
        window.Activate();
        return completion.Task;
    }

    static Window MessageWindow(string text, params Button[] buttons)
    {
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        foreach (var button in buttons)
        {
            buttonPanel.Children.Add(button);
        }

        return new()
        {
            Title = "DiffEngineTray",
            Icon = AppIcons.Default,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        MaxWidth = 460,
                        TextWrapping = TextWrapping.Wrap
                    },
                    buttonPanel
                }
            }
        };
    }

    static Window HiddenWindow() =>
        new()
        {
            Width = 1,
            Height = 1,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            Opacity = 0,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
}
