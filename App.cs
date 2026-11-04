using System;
using System.Windows;

namespace OyunCeviriPC
{
    public class App : Application
    {
        [STAThread]
        public static void Main()
        {
            var app = new App();
            var mainWindow = new MainWindow();
            app.Run(mainWindow);
        }
    }
}
