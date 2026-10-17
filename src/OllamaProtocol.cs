using System;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ScamOllama
{
    public sealed class GenerationSettings
    {
        public string Endpoint = "http://127.0.0.1:11434";
        public string Model = "";
        public int ContextTokens = 16384;
        public int OutputTokens = 2048;
        public float Temperature = 0.7f;
        public float TopP = 0.9f;
        public int TopK = 40;
        public float RepeatPenalty = 1.1f;
        public int Seed = -1;
        public int TimeoutSeconds = 300;
        public string KeepAlive = "10m";
        public string Thinking = "Off";
        public bool StructuredOutput = true;

        public string ValidatedEndpoint()
        {
            Uri uri;
            if (string.IsNullOrWhiteSpace(Endpoint) ||
                !Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https") ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("Enter an HTTP or HTTPS Ollama address without a password, query or fragment.");
            if (!AllowedHost(uri))
                throw new ArgumentException("Use localhost, a Tailscale IP, or the server's MagicDNS name. Public internet servers are not supported.");
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path != "" && path != "/api" && path != "/api/chat" && path != "/api/tags" && path != "/v1" && path != "/v1/chat/completions")
                throw new ArgumentException("Use the Ollama server address, optionally ending in /api or /v1.");
            // Canonicalize for requests only. Keep the entered URL in settings.
            return uri.GetLeftPart(UriPartial.Authority);
        }

        private static bool AllowedHost(Uri uri)
        {
            if (uri.IsLoopback) return true;
            IPAddress address;
            if (IPAddress.TryParse(uri.DnsSafeHost, out address))
            {
                byte[] bytes = address.GetAddressBytes();
                if (bytes.Length == 4) return bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127;
                return bytes.Length == 16 && bytes[0] == 0xfd && bytes[1] == 0x7a &&
                    bytes[2] == 0x11 && bytes[3] == 0x5c && bytes[4] == 0xa1 && bytes[5] == 0xe0;
            }
            string host = uri.DnsSafeHost.TrimEnd('.');
            return uri.HostNameType == UriHostNameType.Dns &&
                (host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase) || host.IndexOf('.') < 0);
        }

        public void Validate()
        {
            ValidatedEndpoint();
            if (string.IsNullOrWhiteSpace(Model) || Model.Length > 256 || Model.Any(char.IsControl))
                throw new ArgumentException("Choose an installed Ollama model in Host Settings.");
            if (Model.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a locally installed model rather than a :cloud model.");
            if (ContextTokens < 2048 || ContextTokens > 262144)
                throw new ArgumentException("Context must be between 2,048 and 262,144 tokens.");
            if (OutputTokens < 64 || OutputTokens >= ContextTokens)
                throw new ArgumentException("Maximum output must be at least 64 and smaller than the context window.");
        }
    }

    // The protocol adapter intentionally has no game or Unity dependencies.
    public static class OllamaProtocol
    {
        public static JObject BuildRequest(JObject source, GenerationSettings settings)
        {
            if (source == null) throw new ArgumentNullException("source");
            settings.Validate();
            var sourceMessages = source["messages"] as JArray;
            if (sourceMessages == null || sourceMessages.Count == 0)
                throw new ArgumentException("The game did not provide conversation messages.");
            var messages = new JArray();
            foreach (JObject original in sourceMessages.OfType<JObject>())
            {
                var content = original["content"];
                string text = content is JArray
                    ? string.Join("\n", ((JArray)content).OfType<JObject>().Where(p => (string)p["type"] == "text").Select(p => (string)p["text"]))
                    : (string)content ?? "";
                messages.Add(new JObject { ["role"] = (string)original["role"] ?? "user", ["content"] = text });
            }
            JToken schema = source["response_format"]?["json_schema"]?["schema"];
            if (schema != null)
            {
                string instruction = (string)source["schema_instruction"] ??
                    "Return only a JSON object matching this schema, without markdown or commentary:\n" + schema.ToString(Formatting.None);
                if ((string)messages[0]["role"] == "system")
                    messages[0]["content"] = (string)messages[0]["content"] + "\n\n" + instruction;
                else
                    messages.Insert(0, new JObject { ["role"] = "system", ["content"] = instruction });
            }
            var options = new JObject
            {
                ["num_ctx"] = settings.ContextTokens,
                ["num_predict"] = settings.OutputTokens,
                ["temperature"] = settings.Temperature,
                ["top_p"] = settings.TopP,
                ["top_k"] = settings.TopK,
                ["repeat_penalty"] = settings.RepeatPenalty
            };
            if (settings.Seed >= 0) options["seed"] = settings.Seed;
            if (source["stop"] != null) options["stop"] = source["stop"].DeepClone();
            var request = new JObject
            {
                ["model"] = settings.Model,
                ["messages"] = messages,
                ["stream"] = false,
                ["keep_alive"] = settings.KeepAlive,
                ["options"] = options
            };
            if (settings.StructuredOutput && schema != null) request["format"] = schema.DeepClone();
            else if (settings.StructuredOutput && (string)source["response_format"]?["type"] == "json_object") request["format"] = "json";
            if (settings.Thinking == "Off") request["think"] = false;
            else if (settings.Thinking == "On") request["think"] = true;
            return request;
        }

        public static JObject WrapResponse(JObject response, string model)
        {
            if (response["error"] != null) throw new InvalidOperationException("Ollama: " + (string)response["error"]);
            string content = (string)response["message"]?["content"];
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("Ollama returned no reply. Try a larger output budget or disable thinking.");
            return new JObject
            {
                ["id"] = "ollama-" + Guid.NewGuid().ToString("N"),
                ["object"] = "chat.completion",
                ["model"] = model,
                ["choices"] = new JArray(new JObject
                {
                    ["index"] = 0,
                    ["message"] = new JObject { ["role"] = "assistant", ["content"] = content },
                    ["finish_reason"] = (string)response["done_reason"] == "length" ? "length" : "stop"
                }),
                ["usage"] = new JObject
                {
                    ["prompt_tokens"] = (int?)response["prompt_eval_count"] ?? 0,
                    ["completion_tokens"] = (int?)response["eval_count"] ?? 0,
                    ["total_tokens"] = ((int?)response["prompt_eval_count"] ?? 0) + ((int?)response["eval_count"] ?? 0)
                }
            };
        }

        public static string[] LocalModels(JObject tags)
        {
            return ((tags["models"] as JArray) ?? new JArray()).OfType<JObject>()
                .Where(m => string.IsNullOrEmpty((string)m["remote_host"]) && string.IsNullOrEmpty((string)m["remote_model"]))
                .Select(m => (string)m["name"] ?? (string)m["model"])
                .Where(n => !string.IsNullOrWhiteSpace(n) && !n.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
                .Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
