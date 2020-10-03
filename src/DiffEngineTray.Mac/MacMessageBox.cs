using AppKit;
using CoreGraphics;
using DiffEngineTray.Common;

namespace DiffEngineTray.Mac
{
    public class MacMessageBox : IMessageBox
    {
        public bool? Show(string message, string title, MessageBoxIcon icon)
        {
            var alert = new NSAlert();
            alert.AlertStyle = NSAlertStyle.Warning;
            alert.MessageText = title;
            alert.InformativeText = message;
            alert.Icon = NSImage.ImageNamed("error.png");
            alert.Icon.Size = new CGSize(16, 16);
            alert.AddButton("Yes");
            alert.AddButton("No");
            var result = alert.RunModal();

            return result == 1000; //Yes
        }
    }
}