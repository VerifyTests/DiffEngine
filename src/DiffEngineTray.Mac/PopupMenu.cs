using System;
using AppKit;
using CoreGraphics;

namespace DiffEngineTray.Mac
{
    public static class PopupMenu
    {
        public static NSStatusItem CreateStatusBarItem()
        {
            var statusBarItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Variable);

            statusBarItem.Button.Image = CreateAppIcon();
            statusBarItem.SendActionOn(NSTouchPhase.Any);

            return statusBarItem;
        }
        
        private static NSImage CreateAppIcon()
        {
            var image = NSImage.ImageNamed("icon.ico");
            image.Size = new CGSize(16, 16);
            image.Template = true;

            return image;
        }
    }
    
    public class PopupMenuItems : NSMenu
    {
        NSMenu menu;

        public Action CloseApp { get; set; }
        public Action ShowOptions { get; set; }
        

        public PopupMenuItems()
        {
            Close = new NSMenuItem("Close", (s, e) => CloseApp?.Invoke());
            Options = new NSMenuItem("Options", (s, e) => ShowOptions?.Invoke());
            OpenLogs = new NSMenuItem("Open Logs");
            RaiseIssue = new NSMenuItem("Raise issue");
            Clear = new NSMenuItem("Clear");
            PendingDeletes = new NSMenuItem("Pending Deletes");
            PendingMoves = new NSMenuItem("Pending Moves");
            AcceptAll = new NSMenuItem("Accept All");

            AssignImages();
            AssignItems();
        }

        void AssignImages()
        {
            Close.Image = NSImage.ImageNamed("exit.png");
            Options.Image = NSImage.ImageNamed("cogs.png");
            OpenLogs.Image = NSImage.ImageNamed("folder.png");
            RaiseIssue.Image = NSImage.ImageNamed("link.png");
            Clear.Image = NSImage.ImageNamed("clear.png");
            PendingDeletes.Image = NSImage.ImageNamed("delete.png");
            PendingMoves.Image = NSImage.ImageNamed("accept.png");
            AcceptAll.Image = NSImage.ImageNamed("acceptAll.png");
        }

        void AssignItems()
        {
            AddItem(Close);
            AddItem(Options);
            AddItem(OpenLogs);
            AddItem(RaiseIssue);
            AddItem(NSMenuItem.SeparatorItem);
            AddItem(Clear);
            AddItem(NSMenuItem.SeparatorItem);
            AddItem(PendingDeletes);
            AddItem(NSMenuItem.SeparatorItem);
            AddItem(PendingMoves);
            AddItem(NSMenuItem.SeparatorItem);
            AddItem(AcceptAll);
        }

        public NSMenuItem Close { get; set; }
        public NSMenuItem Options { get; set; }
        public NSMenuItem OpenLogs { get; set; }
        public NSMenuItem RaiseIssue { get; set; }
        public NSMenuItem Clear { get; set; }
        public NSMenuItem PendingDeletes { get; set; }
        public NSMenuItem PendingMoves { get; set; }
        public NSMenuItem AcceptAll { get; set; }
    }
}