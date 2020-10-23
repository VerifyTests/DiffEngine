using System;
using AppKit;
using CoreGraphics;
using DiffEngineTray.Common;

namespace DiffEngineTray.Mac
{
    public class MacMessageBox : IMessageBox
    {
        public static bool? ShowMessage(string message, string title, MessageBoxIcon icon, MessageBoxButtons buttons = MessageBoxButtons.YesNo)
        {
            return new MacMessageBox().Show(message, title, icon);
        }
        
        public bool? Show(string message, string title, MessageBoxIcon icon, MessageBoxButtons buttons = MessageBoxButtons.YesNo)
        {
            var alert = new NSAlert();
            alert.AlertStyle = NSAlertStyle.Warning;
            alert.MessageText = title;
            alert.InformativeText = message;
            alert.Icon = NSImage.ImageNamed("error.png");
            alert.Icon.Size = new CGSize(16, 16);

            AddButtons(alert, buttons);
            
            var result = alert.RunModal();

            return TranslateResult(result, buttons);
        }

        private bool? TranslateResult(nint result, MessageBoxButtons buttons)
        {
            return result == 1000;
        }

        private void AddButtons(NSAlert alert, MessageBoxButtons butons)
        {
            if (butons == MessageBoxButtons.YesNo)
            {
                alert.AddButton("Yes");
                alert.AddButton("No");
            }
            else
            {
                alert.AddButton("OK");
            }
        }
    }
}