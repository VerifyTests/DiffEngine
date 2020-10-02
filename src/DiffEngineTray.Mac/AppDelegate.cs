using System;
using AppKit;
using Foundation;
using Xamarin.Forms;

namespace DiffEngineTray.Mac
{
    [Register("AppDelegate")]
    public class AppDelegate : NSApplicationDelegate
    {
        NSStatusItem statusBarItem;
        PopupMenuItems popup;
        App app;

        public AppDelegate()
        {
            Forms.Init();
            CreateStatusItems();
            SubscribeEvents();
            Application.SetCurrentApplication(app = new App());
        }

        private void SubscribeEvents()
        {
            popup.CloseApp = () => Exit();
            popup.ShowOptions = () => app.ShowSettings(statusBarItem);
        }

        private void CreateStatusItems()
        {
            // Create the status bar item
            statusBarItem = PopupMenu.CreateStatusBarItem();
            statusBarItem.Button.Activated += StatusItemActivated;
            
            // Create the popup
            popup = new PopupMenuItems();
        }
        
        private void StatusItemActivated(object sender, EventArgs e)
        {
            statusBarItem.PopUpStatusItemMenu(popup);
        }

        void Exit()
        {
            NSApplication.SharedApplication.Terminate(this);
        }

        public override void DidFinishLaunching(NSNotification notification)
        {
            // Insert code here to initialize your application
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }
    }
    
    public class PopoverDelegate : NSPopoverDelegate
    {
        public override void DidClose(NSNotification notification)
        { 
            Application.Current.SendSleep();
        }
    }
}
