using System;
using AppKit;
using CoreGraphics;
using DiffEngineTray.Common;
using Foundation;
using Xamarin.Forms;
using Xamarin.Forms.Platform.MacOS;

namespace DiffEngineTray.Mac
{
    [Register("AppDelegate")]
    public class AppDelegate : NSApplicationDelegate
    {
        private NSStatusItem _statusBarItem;
        private NSMenu _menu;
        private NSViewController _mainPage;

        public AppDelegate()
        {
            Forms.Init();
            CreateStatusItems();
            Application.SetCurrentApplication(new App());
        }

        private void CreateStatusItems()
        {
            // Create the status bar item
            NSStatusBar statusBar = NSStatusBar.SystemStatusBar;
            _statusBarItem = statusBar.CreateStatusItem(NSStatusItemLength.Variable);
            var image = new NSImage("icon.png");
            image.Size = new CGSize(16, 16);
            image.Template = true;
            
            _statusBarItem.Button.Image = image;

            // Listen to touches on the status bar item
            _statusBarItem.SendActionOn(NSTouchPhase.Any);
            _statusBarItem.Button.Activated += StatusItemActivated;
            //_statusBarItem.Action = new ObjCRuntime.Selector("MenuAction:");

            // Create the menu that gets opened on a right click
            _menu = new NSMenu(); 
            var closeAppItem = new NSMenuItem("Close");
            closeAppItem.Activated += CloseAppItem_Activated;
            _menu.AddItem(closeAppItem);
        }

        private void CloseAppItem_Activated(object sender, EventArgs e)
        {
            NSApplication.SharedApplication.Terminate(this);
        }
        
        private void StatusItemActivated(object sender, EventArgs e)
        {
            var currentEvent = NSApplication.SharedApplication.CurrentEvent;
            switch (currentEvent.Type)
            {
                case NSEventType.LeftMouseDown:
                    ShowWindow();
                    break;
                case NSEventType.RightMouseDown: 
                    _statusBarItem.PopUpStatusItemMenu(_menu);
                    break;
            }
        }
        
        private void ShowWindow()
        { 
            if(_mainPage == null)
            {
                // If you dont need a navigation bar, just use this line
                _mainPage = Application.Current.MainPage.CreateViewController(); 
                _mainPage.View.Frame = new CoreGraphics.CGRect(0, 0, 400, 700);

                Application.Current.SendStart();
            }
            else
            {
                Application.Current.SendResume();
            }

            var popover = new NSPopover
            {
                ContentViewController = _mainPage,
                Behavior = NSPopoverBehavior.Transient,
                Delegate = new PopoverDelegate()
            };
            popover.Show(_statusBarItem.Button.Bounds, _statusBarItem.Button, NSRectEdge.MaxYEdge);
        }

        public override void DidFinishLaunching(NSNotification notification)
        {
            // Insert code here to initialize your application
            // var statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Variable);
            // statusItem.Title = "DiffEngineTray";
            //
            // var image = new NSImage("icon.png");
            // image.Size = new CoreGraphics.CGSize(16, 16);
            // image.Template = true;
            //
            // statusItem.Button.Image = image;
            // statusItem.Button.Action = new ObjCRuntime.Selector("StatusItemButtonAction:");
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }
        
        // [Action("StatusItemButtonAction:")]
        // public void StatusItemButtonAction(NSStatusBarButton sender)
        // {
        //     var currentEvent = NSApplication.SharedApplication.CurrentEvent;
        //     if (currentEvent.ModifierFlags.HasFlag(NSEventModifierMask.AlternateKeyMask) ||
        //         currentEvent.ModifierFlags.HasFlag(NSEventModifierMask.ControlKeyMask))
        //         NSApplication.SharedApplication.Terminate(this);
        //     else
        //         StatusBarPopOver();
        // }

        // private void StatusBarPopOver()
        // {
        //     var popover = new NSPopover
        //     {
        //         //ContentViewController = _mainPage,
        //         Behavior = NSPopoverBehavior.Transient,
        //         Delegate = new PopoverDelegate()
        //     };
        //     popover.Show(_statusBarItem.Button.Bounds, _statusBarItem.Button, NSRectEdge.MaxYEdge);
        // }
    }
    
    public class PopoverDelegate : NSPopoverDelegate
    {
        public override void DidClose(NSNotification notification)
        { 
            Application.Current.SendSleep();
        }
    }
}
