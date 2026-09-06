using System;
using System.Windows.Forms;

namespace WinCleaner
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                Exception ex = e.ExceptionObject as Exception;
                string msg = ex != null ? ex.Message : "Unknown fatal error occurred.";
                MessageBox.Show("WinCleaner encountered an unexpected error:\n\n" + msg,
                    "Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.ThreadException += (s, e) =>
            {
                MessageBox.Show("WinCleaner encountered an error:\n\n" + e.Exception.Message,
                    "Application Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            Application.Run(new MainForm());
        }
    }
}
