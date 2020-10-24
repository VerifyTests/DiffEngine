using System.Collections.Generic;
using System.Windows.Forms;
using DiffEngineTray.Common;
using Icon = DiffEngineTray.Common.MessageBoxIcon;
using MessageBoxButtons = DiffEngineTray.Common.MessageBoxButtons;
using WinIcon = System.Windows.Forms.MessageBoxIcon;

namespace DiffEngineTray
{
    public class WindowsMessageBox : IMessageBox
    {
        IWin32Window window;
        Dictionary<Icon, WinIcon> iconMap = new Dictionary<Icon, WinIcon>();

        public WindowsMessageBox(IWin32Window window)
        {
            this.window = window;
        }

        public bool? Show(string message, string title, Icon icon, MessageBoxButtons buttons = MessageBoxButtons.YesNo)
        {
            var mappedIcon = iconMap[icon];
            var result = MessageBox.Show(window, message, title, System.Windows.Forms.MessageBoxButtons.YesNo, mappedIcon);

            return ReturnResult(result);
        }

        bool? ReturnResult(DialogResult result)
        {
            if (result == DialogResult.OK || result == DialogResult.Yes)
                return true;
            if (result == DialogResult.No || result == DialogResult.Abort)
                return false;
            return null;
        }
    }
}