/// <summary>
/// A tray that makes its menu as PlatformHeads' <see cref="NotifyIconTray{TIcon}" /> does, from
/// the last model and by the head's options, and puts no icon in the notification area: a
/// NotifyIconTray shows one as it is made, on the desktop of whoever runs the tests.
/// </summary>
sealed class MenuTray :
    ITray<TrayIcon>
{
    TrayModel<TrayIcon>? model;
    string? clicked;

    public MenuTray(NotifyIconOptions<TrayIcon> options)
    {
        options.Theme?.Invoke(Menu);
        MenuOnOpening.Rebuild(
            Menu,
            items =>
            {
                options.Theme?.Invoke(Menu);
                if (model is null)
                {
                    return;
                }

                foreach (var item in model.Items)
                {
                    items.Add(TrayMenuItems.Create(item, options, _ => clicked = _));
                }
            });
    }

    public System.Windows.Forms.ContextMenuStrip Menu { get; } = new();

    public void Apply(TrayModel<TrayIcon> next) =>
        model = next;

    public void Notify(TrayNotification notification)
    {
    }

    public TrayInput Poll()
    {
        var input = new TrayInput(clicked, false, null);
        clicked = null;
        return input;
    }

    public void Dispose() =>
        Menu.Dispose();
}
