using System;
using System.Threading;
using System.Threading.Tasks;
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
            Application.SetCurrentApplication(app = new App());
            CreateStatusItems();
            SubscribeEvents();
            
            IssueLauncher.Initialize(new MacMessageBox());
        }
        
        void SubscribeEvents()
        {
            popup.CloseApp = () => Exit();
            popup.ShowOptions = () => app.ShowSettings(statusBarItem);
        }

        void CreateStatusItems()
        {
            // Create the status bar item
            statusBarItem = PopupMenu.CreateStatusBarItem();
            statusBarItem.Button.Activated += StatusItemActivated;
            
            // Create the popup
            popup = new PopupMenuItems();
        }
        
        void StatusItemActivated(object sender, EventArgs e)
        {
            statusBarItem.PopUpStatusItemMenu(popup);
        }

        void Exit()
        {
            NSApplication.SharedApplication.Terminate(this);
        }

        public override void DidFinishLaunching(NSNotification notification)
        {
            Task.Run(StartServer);
        }

        public override void WillTerminate(NSNotification notification)
        {
            // Insert code here to tear down your application
        }
        
        async Task StartServer()
        {
            var tokenSource = new CancellationTokenSource();
            var cancellation = tokenSource.Token;
            
            await using var tracker = new Tracker(
                active: () => statusBarItem.Image = NSImage.ImageNamed("activate.ico"),
                inactive: () => statusBarItem.Image = NSImage.ImageNamed("default.ico"));
            
            using var task = StartServer(tracker, cancellation);
        }
        
        static Task StartServer(Tracker tracker, CancellationToken cancellation)
        {
            return PiperServer.Start(
                payload =>
                {
                    tracker.AddMove(
                        payload.Temp,
                        payload.Target,
                        payload.Exe,
                        payload.Arguments,
                        payload.CanKill,
                        payload.ProcessId);
                },
                payload => tracker.AddDelete(payload.File),
                cancellation);
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
