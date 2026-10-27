using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KSACars;

/// <summary>
/// A port opened on the home router through UPnP's Internet Gateway Device, which most home routers
/// answer unless it has been switched off: found by a multicast search, its description read for
/// the WAN connection service, and that asked to forward a UDP port to this machine and for the
/// address it is reached at from outside.
///
/// <para>The parsing is apart from the asking, so what a router answers can be tested without one.</para>
/// </summary>
internal static class Upnp
{
    private static readonly IPEndPoint Ssdp = new(IPAddress.Parse("239.255.255.250"), 1900);

    private static readonly string[] Services =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1",
    ];

    /// <summary>A router's service that forwards ports: where it is asked, and which it is.</summary>
    public readonly record struct Gateway(Uri Control, string Service, IPAddress Local);

    /// <summary>What opening a port came to: the address outside reaches it at, or why there is none.</summary>
    public readonly record struct Opened(Gateway? Gateway, IPAddress? External, string Why);

    public static string SearchRequest(string target) =>
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 2\r\n" +
        $"ST: {target}\r\n\r\n";

    /// <summary>The description's address in an answer to a search, or null.</summary>
    public static Uri? Location(string answer)
    {
        Match m = Regex.Match(answer, @"^LOCATION:\s*(\S+)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
        return m.Success && Uri.TryCreate(m.Groups[1].Value, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttp ? uri : null;
    }

    /// <summary>The WAN connection service in a device's description, the first of those that forward a port, and where it is asked.</summary>
    public static (Uri Control, string Service)? ControlOf(string description, Uri location)
    {
        XDocument doc = XDocument.Parse(description);
        XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        Uri root = doc.Root?.Element(ns + "URLBase")?.Value is { Length: > 0 } b && Uri.TryCreate(b, UriKind.Absolute, out Uri? baseUri) ? baseUri : location;
        foreach (string wanted in Services)
        {
            foreach (XElement service in doc.Descendants(ns + "service"))
            {
                if ((string?)service.Element(ns + "serviceType") != wanted) continue;
                if (service.Element(ns + "controlURL")?.Value is not { Length: > 0 } control) continue;
                return (new Uri(root, control.Trim()), wanted);
            }
        }
        return null;
    }

    public static string AddPortMapping(string service, int port, IPAddress local, string description, int leaseSeconds) =>
        Soap(service, "AddPortMapping",
             $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>" +
             $"<NewInternalPort>{port}</NewInternalPort><NewInternalClient>{local}</NewInternalClient><NewEnabled>1</NewEnabled>" +
             $"<NewPortMappingDescription>{System.Security.SecurityElement.Escape(description)}</NewPortMappingDescription>" +
             $"<NewLeaseDuration>{leaseSeconds}</NewLeaseDuration>");

    public static string DeletePortMapping(string service, int port) =>
        Soap(service, "DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{port}</NewExternalPort><NewProtocol>UDP</NewProtocol>");

    public static string GetExternalIPAddress(string service) => Soap(service, "GetExternalIPAddress", "");

    /// <summary>The address in an answer to <see cref="GetExternalIPAddress"/>, or null.</summary>
    public static IPAddress? ExternalOf(string answer)
    {
        Match m = Regex.Match(answer, @"<NewExternalIPAddress>\s*([^<\s]+)\s*</NewExternalIPAddress>", RegexOptions.IgnoreCase);
        return m.Success && IPAddress.TryParse(m.Groups[1].Value, out IPAddress? ip) ? ip : null;
    }

    /// <summary>A SOAP fault's own words, for saying why a router refused: its UPnP error code and description, or nothing.</summary>
    public static string FaultOf(string answer)
    {
        Match code = Regex.Match(answer, @"<errorCode>\s*(\d+)\s*</errorCode>", RegexOptions.IgnoreCase);
        Match text = Regex.Match(answer, @"<errorDescription>\s*([^<]*?)\s*</errorDescription>", RegexOptions.IgnoreCase);
        return code.Success ? $"error {code.Groups[1].Value}{(text.Success ? $", {text.Groups[1].Value}" : "")}" : "";
    }

    private static string Soap(string service, string action, string arguments) =>
        "<?xml version=\"1.0\"?>" +
        "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
        $"<s:Body><u:{action} xmlns:u=\"{service}\">{arguments}</u:{action}></s:Body></s:Envelope>";

    /// <summary>
    /// Has the router forward a UDP port to this machine, for a lease that a crash leaves to run
    /// out rather than open for good, and asks it the address it is reached at from outside.
    /// </summary>
    public static async Task<Opened> OpenAsync(int port, string description, int leaseSeconds, CancellationToken token)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(4) };
        Gateway? found;
        try
        {
            found = await FindAsync(http, token);
        }
        catch (Exception e) when (e is SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or System.Xml.XmlException)
        {
            return new Opened(null, null, $"no router answered: {e.Message}");
        }
        if (found is not { } gateway) return new Opened(null, null, "no router answered for UPnP, or it has UPnP switched off");

        try
        {
            string added = await PostAsync(http, gateway, "AddPortMapping", AddPortMapping(gateway.Service, port, gateway.Local, description, leaseSeconds), token);
            // Some routers refuse a lease and take only a mapping for good, which is then taken down when the race is.
            if (FaultOf(added) is { Length: > 0 } && leaseSeconds != 0)
            {
                added = await PostAsync(http, gateway, "AddPortMapping", AddPortMapping(gateway.Service, port, gateway.Local, description, 0), token);
            }
            if (FaultOf(added) is { Length: > 0 } refused) return new Opened(gateway, null, $"the router refused to open the port: {refused}");
            string answer = await PostAsync(http, gateway, "GetExternalIPAddress", GetExternalIPAddress(gateway.Service), token);
            return ExternalOf(answer) is { } external
                ? new Opened(gateway, external, "")
                : new Opened(gateway, null, "the router opened the port and would not say its own address");
        }
        catch (Exception e) when (e is SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            return new Opened(gateway, null, $"the router stopped answering: {e.Message}");
        }
    }

    /// <summary>Closes a port <see cref="OpenAsync"/> opened. Quietly: a router that has gone has closed it.</summary>
    public static async Task CloseAsync(Gateway gateway, int port)
    {
        try
        {
            using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
            await PostAsync(http, gateway, "DeletePortMapping", DeletePortMapping(gateway.Service, port), CancellationToken.None);
        }
        catch (Exception e) when (e is SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    private static async Task<Gateway?> FindAsync(HttpClient http, CancellationToken token)
    {
        using UdpClient udp = new(AddressFamily.InterNetwork);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
        foreach (string target in (string[])["urn:schemas-upnp-org:device:InternetGatewayDevice:1", "upnp:rootdevice"])
        {
            byte[] ask = Encoding.ASCII.GetBytes(SearchRequest(target));
            await udp.SendAsync(ask, Ssdp, token);
            await udp.SendAsync(ask, Ssdp, token);
        }

        HashSet<Uri> tried = [];
        using CancellationTokenSource window = CancellationTokenSource.CreateLinkedTokenSource(token);
        window.CancelAfter(TimeSpan.FromSeconds(3));
        while (!window.IsCancellationRequested)
        {
            UdpReceiveResult got;
            try { got = await udp.ReceiveAsync(window.Token); }
            catch (OperationCanceledException) { break; }

            if (Location(Encoding.ASCII.GetString(got.Buffer)) is not { } location || !tried.Add(location)) continue;
            try
            {
                string description = await http.GetStringAsync(location, token);
                if (ControlOf(description, location) is not { } control) continue;
                return new Gateway(control.Control, control.Service, LocalTowards(got.RemoteEndPoint.Address));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
            {
            }
        }
        return null;
    }

    /// <summary>The address of this machine on the way to another: what a route there goes out from. Nothing is sent.</summary>
    public static IPAddress LocalTowards(IPAddress router)
    {
        using Socket probe = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Connect(router, 1900);
        return ((IPEndPoint)probe.LocalEndPoint!).Address;
    }

    private static async Task<string> PostAsync(HttpClient http, Gateway gateway, string action, string body, CancellationToken token)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, gateway.Control);
        request.Content = new StringContent(body, Encoding.UTF8, "text/xml");
        request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{gateway.Service}#{action}\"");
        using HttpResponseMessage response = await http.SendAsync(request, token);
        // A refusal is a 500 with the fault in its body, which is what says why.
        return await response.Content.ReadAsStringAsync(token);
    }
}
