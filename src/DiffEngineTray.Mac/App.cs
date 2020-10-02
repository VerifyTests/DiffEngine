using System.Diagnostics;
using AppKit;
using CoreGraphics;
using Xamarin.Forms;
using Xamarin.Forms.Platform.MacOS;

namespace DiffEngineTray.Mac
{
    public class App : Application
    {
        NSViewController controller;

        public void CreateMainController()
        {
            MainPage = new NavigationPage(new MainPage());
            controller = MainPage.CreateViewController();
            controller.View.Frame = new CGRect(0, 0, 300, 400);
        }

        protected override void OnStart()
        {
            base.OnStart();
            Debug.WriteLine("Application started");
        }

        protected override void OnSleep()
        {
            base.OnSleep();
            Debug.WriteLine("Application sleeps");
        }

        protected override void OnResume()
        {
            base.OnResume();
            Debug.WriteLine("Application resumes");
        }

        public void ShowSettings(NSStatusItem statusBarItem)
        {
            if(MainPage == null)
            {
                CreateMainController();
                Current.SendStart();
            }
            
            var popover = new NSPopover
            {
                ContentViewController = controller,
                Behavior = NSPopoverBehavior.Transient,
                Delegate = new PopoverDelegate()
            };
            
            popover.Show(statusBarItem.Button.Bounds, statusBarItem.Button, NSRectEdge.MaxYEdge);
        }
    }
}