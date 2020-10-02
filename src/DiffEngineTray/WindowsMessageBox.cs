using System.Collections.Generic;
using System.Windows.Forms;
using DiffEngineTray.Common;
using Icon = DiffEngineTray.Common.MessageBoxIcon;
using WinIcon = System.Windows.Forms.MessageBoxIcon;

namespace DiffEngineTray
{
    public class WindowsMessageBox : IMessageBox
    {
        Dictionary<Icon, WinIcon> iconMap = new Dictionary<Icon, WinIcon>();
        
        public bool? Show(string message, string title, Icon icon)
        {
            var mappedIcon = iconMap[icon];
            var result = MessageBox.Show(message, title, MessageBoxButtons.YesNo, mappedIcon);

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