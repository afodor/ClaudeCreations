using System.Web;

namespace ScatterPlotExplorer;

public partial class App : System.Windows.Application
{
    public static string? JournalArg { get; private set; }
    public static int? EntryArg { get; private set; }

    private void Application_Startup(object sender, System.Windows.StartupEventArgs e)
    {
        RegisterProtocolHandler();
        ParseArgs(e.Args);
        var win = new MainWindow();
        win.Show();
    }

    private void ParseArgs(string[] args)
    {
        // Check for protocol URL: scatterplot://open?journal=...&entry=...
        // When launched via protocol, the entire URL is typically the first arg
        if (args.Length >= 1 && args[0].StartsWith("scatterplot://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(args[0]);
                var query = HttpUtility.ParseQueryString(uri.Query);
                string? journal = query["journal"];
                if (!string.IsNullOrEmpty(journal))
                    JournalArg = journal;
                string? entry = query["entry"];
                if (int.TryParse(entry, out int idx))
                    EntryArg = idx;
            }
            catch { }
            return;
        }

        // Check for command-line args: --journal "path" --entry N
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--journal", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                JournalArg = args[++i];
            else if (args[i].Equals("--entry", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out int idx))
                    EntryArg = idx;
            }
        }
    }

    private static void RegisterProtocolHandler()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\scatterplot");
            key.SetValue("", "URL:Scatter Plot Explorer");
            key.SetValue("URL Protocol", "");
            using var iconKey = key.CreateSubKey("DefaultIcon");
            iconKey.SetValue("", $"\"{exePath}\",0");
            using var cmdKey = key.CreateSubKey(@"shell\open\command");
            cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
        }
        catch { }
    }
}
