using System.Windows.Forms;

public partial class OptionsForm :
    Form
{
    public OptionsForm()
    {
        InitializeComponent();
        Icon = Images.Active;
    }

    public Settings Settings { get; set; } = null!;
}