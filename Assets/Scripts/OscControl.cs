using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Minimal OSC-over-UDP control (no dependencies). Listens on _listenPort and replies
// the full settings as /quest/* messages to the last sender's IP on _replyPort.
// Addresses: /fps f  /res i  /eye i  /mirror i  /invert i  /min f  /max f  /near i
//            /tooclose f  /toofar f  /unknown f  /encoding i  /lowpower i  /kinectscale i  /color i  /colorfps i  /status
public sealed class OscControl : MonoBehaviour
{
    [SerializeField] DepthToNdiBridge _bridge;
    [SerializeField] int _listenPort = 9000;
    [SerializeField] int _replyPort = 9001;

    readonly ConcurrentQueue<(string address, float value, IPAddress from)> _queue = new();
    UdpClient _udp;
    Thread _thread;
    IPAddress _lastSender;

    void OnEnable()
    {
        _udp = new UdpClient(_listenPort);
        _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OscControl" };
        _thread.Start();
        Debug.Log($"DepthToNdi: OSC listening on UDP {_listenPort}");
    }

    void OnDisable()
    {
        _udp?.Close();
        _thread = null;
    }

    void ReceiveLoop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_thread != null)
        {
            try
            {
                var data = _udp.Receive(ref any);
                if (TryParse(data, out var address, out var value)) _queue.Enqueue((address, value, any.Address));
            }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }
        }
    }

    void Update()
    {
        var changed = false;
        var statusRequested = false;
        while (_queue.TryDequeue(out var msg))
        {
            _lastSender = msg.from;
            if (msg.address == "/status") statusRequested = true;
            else changed |= Handle(msg.address, msg.value);
        }
        if (changed) _bridge.ApplySettings(); // triggers SendStatus via SettingsApplied
        else if (statusRequested) SendStatus();
    }

    void Start() => _bridge.SettingsApplied += SendStatus;

    bool Handle(string address, float v)
    {
        var s = _bridge.Settings;
        switch (address)
        {
            case "/fps":      s.fps = Mathf.RoundToInt(v); break;
            case "/res":      s.resolution = Mathf.RoundToInt(v); break;
            case "/eye":      s.eye = Mathf.RoundToInt(v); break;
            case "/mirror":   s.mirror = v > 0.5f; break;
            case "/invert":   s.invert = v > 0.5f; break;
            case "/min":      s.minDepth = v; break;
            case "/max":      s.maxDepth = v; break;
            case "/near":     s.SetNearMode(v > 0.5f); break;
            case "/tooclose": s.tooCloseValue = v; break;
            case "/toofar":   s.tooFarValue = v; break;
            case "/unknown":  s.unknownValue = v; break;
            case "/encoding": s.encoding = (DepthStreamSettings.Encoding)Mathf.Clamp(Mathf.RoundToInt(v), 0, 2); break;
            case "/lowpower": s.lowPower = v > 0.5f; break;
            case "/kinectscale": s.kinectScale = v > 0.5f; break;
            case "/color":    s.colorStream = v > 0.5f; break;
            case "/align":    s.alignToColor = v > 0.5f; break;
            case "/colorflip": s.colorFlip = v > 0.5f; break;
            case "/colorfps": s.colorFps = Mathf.Clamp(Mathf.RoundToInt(v), 1, 60); break;
            default: Debug.LogWarning($"DepthToNdi: unknown OSC address {address}"); return false;
        }
        return true;
    }

    void SendStatus()
    {
        if (_lastSender == null || _udp == null) return;
        var s = _bridge.Settings;
        var ep = new IPEndPoint(_lastSender, _replyPort);
        void Send(string a, float v) { var b = Build(a, v); _udp.Send(b, b.Length, ep); }
        try
        {
            Send("/quest/fps", s.EffectiveFps);
            Send("/quest/res", s.EffectiveResolution);
            Send("/quest/eye", s.eye);
            Send("/quest/mirror", s.mirror ? 1 : 0);
            Send("/quest/invert", s.invert ? 1 : 0);
            Send("/quest/min", s.minDepth);
            Send("/quest/max", s.maxDepth);
            Send("/quest/tooclose", s.tooCloseValue);
            Send("/quest/toofar", s.tooFarValue);
            Send("/quest/unknown", s.unknownValue);
            Send("/quest/encoding", (int)s.encoding);
            Send("/quest/lowpower", s.lowPower ? 1 : 0);
            Send("/quest/kinectscale", s.kinectScale ? 1 : 0);
            Send("/quest/color", s.colorStream ? 1 : 0);
            Send("/quest/colorfps", s.colorFps);
            Send("/quest/colorflip", s.colorFlip ? 1 : 0);
            Send("/quest/align", s.alignToColor ? 1 : 0);
        }
        catch (SocketException e) { Debug.LogWarning($"DepthToNdi: OSC reply failed: {e.Message}"); }
    }

    // --- Minimal OSC codec: one message, first numeric argument (i/f/d/T/F) or none ---

    static bool TryParse(byte[] d, out string address, out float value)
    {
        address = null; value = 0;
        if (d.Length < 4 || d[0] != '/') return false; // bundles ('#bundle') not supported
        var p = 0;
        address = ReadString(d, ref p);
        if (p >= d.Length) return true;                 // no type tags: e.g. "/status"
        var tags = ReadString(d, ref p);
        if (tags.Length < 2) return true;
        switch (tags[1])
        {
            case 'i': value = ReadInt(d, p); break;
            case 'f': value = BitConverter.Int32BitsToSingle(ReadInt(d, p)); break;
            case 'd':
                var hi = (long)(uint)ReadInt(d, p); var lo = (long)(uint)ReadInt(d, p + 4);
                value = (float)BitConverter.Int64BitsToDouble((hi << 32) | lo); break;
            case 'T': value = 1; break;
            case 'F': value = 0; break;
            case 's': float.TryParse(ReadString(d, ref p), System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out value); break;
        }
        return true;
    }

    static string ReadString(byte[] d, ref int p)
    {
        var start = p;
        while (p < d.Length && d[p] != 0) p++;
        var s = Encoding.ASCII.GetString(d, start, p - start);
        p = (p + 4) & ~3; // skip null terminator + padding
        return s;
    }

    static int ReadInt(byte[] d, int p) =>
        p + 4 > d.Length ? 0 : (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

    static byte[] Build(string address, float value)
    {
        var a = Padded(address);
        var t = Padded(",f");
        var b = new byte[a.Length + t.Length + 4];
        Buffer.BlockCopy(a, 0, b, 0, a.Length);
        Buffer.BlockCopy(t, 0, b, a.Length, t.Length);
        var bits = BitConverter.SingleToInt32Bits(value);
        var o = a.Length + t.Length;
        b[o] = (byte)(bits >> 24); b[o + 1] = (byte)(bits >> 16); b[o + 2] = (byte)(bits >> 8); b[o + 3] = (byte)bits;
        return b;
    }

    static byte[] Padded(string s)
    {
        var b = new byte[(s.Length + 4) & ~3];
        Encoding.ASCII.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }
}
