using System;
using System.Threading;
using System.Windows.Forms;
using Aerocord.UI;

namespace Aerocord
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            Application.ThreadException += (s, e) =>
                MessageBox.Show("Unexpected error: " + e.Exception.Message, "Aerocord",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show("Fatal error: " + (e.ExceptionObject as Exception)?.Message, "Aerocord",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            Application.Run(new LoginForm());
        }
    }
}
