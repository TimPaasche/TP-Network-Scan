using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Spectre.Console;

namespace TP_Network_Scan;

internal class Program
{
    private static void Main(string[] args)
    {
        AnsiConsole.Write(new FigletText("TP-Network-Scan").Centered().Color(Color.Green));
        AnsiConsole.Markup("[orange1]This Network scanner can only be used for class C networks![/]\n");

        var mode = AnsiConsole.Prompt(
            new TextPrompt<string>("In which mode should the network be tested?")
                .AddChoices(["Auto", "Manual"])
                .DefaultValue("Auto"));
        AnsiConsole.MarkupLine($"[blue]Mode is set to {mode}[/]\n");

        try
        {
            if (mode == "Auto") AutomaticScanNetwork();
            if (mode == "Manual") ManualScanNetwork();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }

        AnsiConsole.WriteLine("\n");

        var exit = false;
        while (!exit)
            exit = AnsiConsole.Prompt(
                new TextPrompt<bool>("Exit the program?")
                    .AddChoice(true)
                    .AddChoice(false)
                    .DefaultValue(false)
                    .WithConverter(choice => choice ? "y" : "n"));
    }

    private static void ManualScanNetwork()
    {
        var localIpAddress = GetLocalIpAddress();
        AnsiConsole.MarkupLine($"Your IP-Address is [orange1]{localIpAddress}[/].");

        var startIpAddressString = AnsiConsole.Ask<string>("Enter the start IP address: ");
        var startIpAddressValid = IPAddress.TryParse(startIpAddressString, out var startIpAddress);
        while (startIpAddressValid == false)
        {
            AnsiConsole.MarkupLine("[red]Invalid IP address entered.[/]");
            startIpAddressString = AnsiConsole.Ask<string>("Enter the start IP address: ");
            startIpAddressValid = IPAddress.TryParse(startIpAddressString, out startIpAddress);
        }

        var stopIpAddressString = AnsiConsole.Ask<string>("Enter the stop IP address: ");
        var stopIpAddressValid = IPAddress.TryParse(stopIpAddressString, out var stopIpAddress);
        while (stopIpAddressValid == false)
        {
            AnsiConsole.MarkupLine("[red]Invalid IP address entered.[/]");
            stopIpAddressString = AnsiConsole.Ask<string>("Enter the stop IP address: ");
            stopIpAddressValid = IPAddress.TryParse(stopIpAddressString, out stopIpAddress);
        }
        ScanForIpAddresses(localIpAddress, startIpAddress, stopIpAddress);
    }

    private static void AutomaticScanNetwork()
    {
        var localIpAddress = GetLocalIpAddress();
        var startIpAddressBytes = localIpAddress.GetAddressBytes();
        startIpAddressBytes[3] = 0;
        var startIpAddress = new IPAddress(startIpAddressBytes);
        var stopIpAddressBytes = localIpAddress.GetAddressBytes();
        stopIpAddressBytes[3] = 255;
        var stopIpAddress = new IPAddress(stopIpAddressBytes);

        AnsiConsole.MarkupLine($"Your IP-Address is [orange1]{localIpAddress}[/].");
        ScanForIpAddresses(localIpAddress, startIpAddress, stopIpAddress);
    }

    private static void ScanForIpAddresses(IPAddress localIpAddress, IPAddress startIpAddress, IPAddress stopIpAddress)
    {
        AnsiConsole.MarkupLine(
            $"The Program will scan for devices in the range from [orange1]{startIpAddress}[/] to [orange1]{stopIpAddress}[/]\n");
        Dictionary<IPAddress, (string name, bool online)> localNetwork = new();

        var confirmation = AnsiConsole.Prompt(
            new TextPrompt<bool>("Run scanner?")
                .AddChoice(true)
                .AddChoice(false)
                .DefaultValue(true)
                .WithConverter(choice => choice ? "y" : "n"));

        if (!confirmation) return;

        Console.Clear();

        Table table = new();
        table.Border(TableBorder.Rounded);
        table.Expand();

        AnsiConsole.Live(table)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .Start(ctx =>
            {
                table.AddColumns("IP-Address", "Hostname", "Status");
                ctx.Refresh();

                for (int i = startIpAddress.GetAddressBytes()[3]; i <= stopIpAddress.GetAddressBytes()[3]; i++)
                {
                    var currentIpAddressBytes = localIpAddress.GetAddressBytes();
                    currentIpAddressBytes[3] = (byte)i;
                    var currentIpAddress = new IPAddress(currentIpAddressBytes);
                    var status = TryGetDeviceInformation(currentIpAddress, out string hostName)
                        ? "[green]Online[/]"
                        : "[red]Offline[/]";
                    table.AddRow(currentIpAddress.ToString(), hostName, status);
                    ctx.Refresh();
                    localNetwork.TryAdd(currentIpAddress, (hostName, status == "[green]Online[/]"));
                }

                return Task.CompletedTask;
            });

        Console.Clear();

        var rule = new Rule("[Green]Results[/]");
        AnsiConsole.Write(rule);
        AnsiConsole.WriteLine("\n");

        Table resultTable = new();
        resultTable.Border(TableBorder.Rounded);
        resultTable.Expand();
        resultTable.AddColumns("IP-Address", "Hostname", "Status");

        foreach (var (ip, (name, online)) in localNetwork)
        {
            if (online == false) continue;
            resultTable.AddRow(ip.ToString(), name, online ? "[green]Online[/]" : "[red]Offline[/]");
        }

        AnsiConsole.Write(resultTable);

        var totalDevices = localNetwork.Count;
        var onlineDevices = localNetwork.Count(x => x.Value.online);

        var onlinePercentage = float.Round((float)onlineDevices / totalDevices * 100, MidpointRounding.ToZero);
        var offlinePercentage = 100 - onlinePercentage;

        AnsiConsole.Write(new BreakdownChart()
            .Width(60)
            .AddItem("Online", onlinePercentage, Color.Green)
            .AddItem("Offline", offlinePercentage, Color.Red));
    }

    private static IPAddress GetLocalIpAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                return ip;

        throw new Exception("No network adapters with an IPv4 address in the system!");
    }

    private static bool TryGetDeviceInformation(IPAddress ipAddress, out string hostName)
    {
        try
        {
            var ping = new Ping();
            var reply = ping.Send(ipAddress, 20);

            if (reply.Status == IPStatus.Success)
            {
                var hostEntry = Dns.GetHostEntry(ipAddress);
                hostName = hostEntry.HostName;
                return true;
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
        }

        hostName = "";
        return false;
    }
}