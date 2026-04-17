using System.Threading;
using DiscordEchoFix.App;

namespace DiscordEchoFix;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, name: "Global\\DiscordEchoFix.SingleInstance", out var createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
