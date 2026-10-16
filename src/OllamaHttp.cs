using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScamOllama
{
    // Windows HTTP avoids POST APIs stripped from the game's Mono System.dll.
    // No replacement framework assemblies are loaded into the game.
    public static class OllamaHttp
    {
        private sealed class Handle : IDisposable
        {
            private IntPtr value;
            public IntPtr Value { get { return value; } }
            public Handle(IntPtr pointer) { if (pointer == IntPtr.Zero) Fail(); value = pointer; }
            public void Dispose() { IntPtr pointer = Interlocked.Exchange(ref value, IntPtr.Zero); if (pointer != IntPtr.Zero) WinHttpCloseHandle(pointer); }
        }

        public static Task<JObject> Send(string url, JObject body, int timeoutSeconds, CancellationToken token)
        {
            var uri = new Uri(url);
            new GenerationSettings { Endpoint = uri.GetLeftPart(UriPartial.Authority) }.ValidatedEndpoint();
            byte[] payload = body == null ? null : Encoding.UTF8.GetBytes(body.ToString(Formatting.None));
            return Task.Run(() => SendBlocking(uri, payload, timeoutSeconds, token), token);
        }

        private static JObject SendBlocking(Uri uri, byte[] payload, int timeoutSeconds, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var session = new Handle(WinHttpOpen("ScamOllama/1.0", 1, null, null, 0)))
            using (var connection = new Handle(WinHttpConnect(session.Value, uri.DnsSafeHost, (ushort)uri.Port, 0)))
            using (var request = new Handle(WinHttpOpenRequest(connection.Value, payload == null ? "GET" : "POST", uri.PathAndQuery, null, null, IntPtr.Zero, uri.Scheme == "https" ? 0x00800000u : 0u)))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                using (deadline.Token.Register(request.Dispose))
                {
                    try
                    {
                        uint disabledFeatures = 2;
                        Ensure(WinHttpSetOption(request.Value, 63, ref disabledFeatures, 4)); // WINHTTP_OPTION_DISABLE_FEATURE
                        int timeout = timeoutSeconds * 1000;
                        Ensure(WinHttpSetTimeouts(request.Value, Math.Min(timeout, 10000), Math.Min(timeout, 10000), timeout, timeout));
                        string headers = payload == null ? "Accept: application/json\r\n" : "Content-Type: application/json\r\nAccept: application/json\r\n";
                        Ensure(WinHttpSendRequest(request.Value, headers, (uint)headers.Length, payload, (uint)(payload == null ? 0 : payload.Length), (uint)(payload == null ? 0 : payload.Length), IntPtr.Zero));
                        Ensure(WinHttpReceiveResponse(request.Value, IntPtr.Zero));
                        uint status, length = 4, index = 0;
                        Ensure(WinHttpQueryHeaders(request.Value, 19u | 0x20000000u, null, out status, ref length, ref index));
                        string text;
                        using (var output = new MemoryStream())
                        {
                            var buffer = new byte[8192];
                            uint count;
                            while (true)
                            {
                                deadline.Token.ThrowIfCancellationRequested();
                                Ensure(WinHttpReadData(request.Value, buffer, (uint)buffer.Length, out count));
                                if (count == 0) break;
                                if (output.Length + count > 16 * 1024 * 1024) throw new InvalidOperationException("Ollama's response exceeded 16 MB.");
                                output.Write(buffer, 0, (int)count);
                            }
                            text = Encoding.UTF8.GetString(output.ToArray());
                        }
                        if (status < 200 || status >= 300)
                        {
                            string detail = status >= 300 && status < 400 ? "Redirects are disabled for local inference." : "Check Ollama and the selected model.";
                            try { detail = (string)JObject.Parse(text)["error"] ?? detail; } catch (JsonException) { }
                            throw new InvalidOperationException("Ollama HTTP " + status + ": " + detail);
                        }
                        return JObject.Parse(text);
                    }
                    catch (Exception)
                    {
                        token.ThrowIfCancellationRequested();
                        if (deadline.IsCancellationRequested) throw new TimeoutException("Ollama timed out. Increase the timeout, lower context, or choose a smaller model.");
                        throw;
                    }
                }
            }
        }

        private static void Ensure(bool success) { if (!success) Fail(); }
        private static void Fail()
        {
            int code = Marshal.GetLastWin32Error();
            throw new InvalidOperationException("Cannot communicate with local Ollama (Windows HTTP error " + code + "). Start Ollama and check its address; for a slow model, increase the request timeout.");
        }

        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpOpen(string agent, uint accessType, string proxy, string bypass, uint flags);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpConnect(IntPtr session, string server, ushort port, uint reserved);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpOpenRequest(IntPtr connect, string verb, string path, string version, string referer, IntPtr acceptTypes, uint flags);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpSetOption(IntPtr request, uint option, ref uint buffer, uint length);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpSetTimeouts(IntPtr request, int resolve, int connect, int send, int receive);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpSendRequest(IntPtr request, string headers, uint headerLength, byte[] optional, uint optionalLength, uint totalLength, IntPtr context);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpQueryHeaders(IntPtr request, uint infoLevel, string name, out uint buffer, ref uint length, ref uint index);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpReadData(IntPtr request, [Out] byte[] buffer, uint bytesToRead, out uint bytesRead);
        [DllImport("winhttp.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool WinHttpCloseHandle(IntPtr handle);
    }
}
