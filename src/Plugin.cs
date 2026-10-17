using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


using Cysharp.Threading.Tasks;

using Mirror;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using Label = UnityEngine.UIElements.Label;

[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]
[assembly: AssemblyProduct("ScamOllama")]

namespace ScamOllama
{
    public sealed class Plugin
    {
        internal static readonly Plugin Instance = new Plugin();
        internal const string ProviderLabel = "Ollama (Local / Tailscale)";
        private ConfigEntry<string> endpoint, model, thinking, keepAlive;
        private ConfigEntry<int> context, output, topK, seed, timeout;
        private ConfigEntry<float> temperature, topP, repeatPenalty;
        private ConfigEntry<bool> structured;
        private readonly LocalConfig Config = new LocalConfig();
        internal readonly SemaphoreSlim RequestGate = new SemaphoreSlim(1, 1);
        internal CancellationTokenSource Lifetime = new CancellationTokenSource();
        internal static bool Selected { get { return PlayerPrefs.GetInt("HostAI.Backend", 0) == 2; } }
        internal static bool Active { get { return Selected && NetworkServer.active && NetworkServer.listen; } }

        private Plugin()
        {

            endpoint = Config.Bind("Ollama", "Endpoint", "http://127.0.0.1:11434", "Local Ollama server, with no /api suffix.");
            model = Config.Bind("Ollama", "Model", "", "Installed model name. Choose in Host Settings.");
            context = Config.Bind("Ollama", "ContextTokens", 16384, new ConfigDescription("Total context window (input plus output).", new AcceptableValueRange<int>(2048, 262144)));
            output = Config.Bind("Ollama", "OutputTokens", 2048, new ConfigDescription("Maximum generated tokens per reply.", new AcceptableValueRange<int>(64, 262143)));
            temperature = Config.Bind("Generation", "Temperature", 0.7f, new ConfigDescription("Creativity.", new AcceptableValueRange<float>(0f, 2f)));
            topP = Config.Bind("Generation", "TopP", 0.9f, new ConfigDescription("Nucleus sampling.", new AcceptableValueRange<float>(0.01f, 1f)));
            topK = Config.Bind("Generation", "TopK", 40, new ConfigDescription("Top-k sampling, 0 disables.", new AcceptableValueRange<int>(0, 1000)));
            repeatPenalty = Config.Bind("Generation", "RepeatPenalty", 1.1f, new ConfigDescription("Repetition penalty.", new AcceptableValueRange<float>(0f, 2f)));
            seed = Config.Bind("Generation", "Seed", -1, new ConfigDescription("-1 uses a random seed.", new AcceptableValueRange<int>(-1, int.MaxValue)));
            timeout = Config.Bind("Ollama", "TimeoutSeconds", 300, new ConfigDescription("Per-request timeout, including queue time.", new AcceptableValueRange<int>(10, 1800)));
            thinking = Config.Bind("Generation", "Thinking", "Off", new ConfigDescription("Off, On, or Model default. Requires model support.", new AcceptableValueList<string>("Off", "On", "Model default")));
            keepAlive = Config.Bind("Ollama", "KeepAlive", "10m", new ConfigDescription("How long to keep the model in memory.", new AcceptableValueList<string>("0", "5m", "10m", "30m", "1h", "-1")));
            structured = Config.Bind("Generation", "StructuredOutput", true, "Use the game's JSON schema as Ollama's output grammar.");


            Debug.Log("[ScamOllama] Direct mod initialized (v1.0.3). Open Settings > Host > AI Backend.");
        }

        internal GenerationSettings Snapshot()
        {
            return new GenerationSettings
            {
                Endpoint = endpoint.Value, Model = model.Value, ContextTokens = context.Value,
                OutputTokens = output.Value, Temperature = temperature.Value, TopP = topP.Value,
                TopK = topK.Value, RepeatPenalty = repeatPenalty.Value, Seed = seed.Value,
                TimeoutSeconds = timeout.Value, Thinking = thinking.Value, KeepAlive = keepAlive.Value,
                StructuredOutput = structured.Value
            };
        }

        public static int InputBudget() { return Selected && Instance != null ? Math.Max(512, Instance.context.Value - Instance.output.Value - 512) : 10000; }
        public static int PairBudget() { return Selected ? 8192 : 128; }
        internal static void Log(string message) { Debug.Log("[ScamOllama] " + message); }
        internal static void Error(Exception error) { Debug.LogWarning("[ScamOllama] " + error.Message); }

        internal static Task<JObject> Http(string url, JObject body, int timeoutSeconds, CancellationToken token)
        { return OllamaHttp.Send(url, body, timeoutSeconds, token); }

        internal static async Task<JObject> Complete(JObject body, CancellationToken cancellationToken)
        {
            if (!Active) throw new InvalidOperationException("Local AI requests require the active lobby host.");
            var plugin = Instance;
            var settings = plugin.Snapshot();
            var request = OllamaProtocol.BuildRequest(body, settings);
            using (var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, plugin.Lifetime.Token))
            {
                lifetime.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
                bool acquired = false;
                try
                {
                    await plugin.RequestGate.WaitAsync(lifetime.Token);
                    acquired = true;
                    var response = await Http(settings.ValidatedEndpoint() + "/api/chat", request, settings.TimeoutSeconds, lifetime.Token);
                    return OllamaProtocol.WrapResponse(response, settings.Model);
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested || plugin.Lifetime.IsCancellationRequested) throw;
                    throw new InvalidOperationException("Ollama timed out. Increase the timeout, lower context, or choose a smaller model.");
                }
                catch (Exception e) { Error(e); throw; }
                finally { if (acquired) plugin.RequestGate.Release(); }
            }
        }

        internal sealed class LocalPanel : IDisposable
        {
            private readonly Plugin plugin;
            private readonly VisualElement content;
            private readonly VisualElement hostScreen;
            private readonly DropdownField backend;
            private readonly DropdownField models;
            private readonly Label status;
            private readonly Button refresh;
            private readonly Button test;
            private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
            private bool attempted, busy, disposed;
            private string statusText = "Start Ollama, then refresh to list installed models.";

            internal LocalPanel(VisualElement root)
            {
                plugin = Instance;
                backend = root.Q<DropdownField>("host-ai-backend");
                hostScreen = root.Q("host-settings-screen");
                backend.choices = new List<string> { "Official Backend", "OpenRouter (Bring Your Own Key)", ProviderLabel };
                content = new VisualElement { name = "ollama-panel" };
                content.style.marginTop = 12;
                content.style.marginBottom = 16;
                content.AddToClassList("openrouter-panel");
                var original = root.Q("openrouter-panel");
                original.parent.Insert(original.parent.IndexOf(original) + 1, content);
                AddHelp("Use Ollama on this PC or a trusted PC over Tailscale. Models and GPU processing belong to the selected server.");
                Text("Ollama address", plugin.endpoint, "Enter localhost or the other PC's Tailscale IP/MagicDNS URL and port. /api and /v1 suffixes are accepted. Edits save immediately.");
                models = new DropdownField { name = "ollama-model", choices = new List<string> { string.IsNullOrEmpty(plugin.model.Value) ? "Refresh to find installed models" : plugin.model.Value } };
                models.SetValueWithoutNotify(models.choices[0]);
                models.AddToClassList("setting-dropdown");
                Row("Installed model", "Models are read from your local Ollama server.").Add(models);
                models.RegisterValueChangedCallback(evt => { if (HostAiSettings.CanEdit && !busy) { plugin.model.Value = evt.newValue; statusText = "Selected " + evt.newValue + ". Applies to the next AI request."; Refresh(); } });
                var actions = new VisualElement();
                actions.style.flexDirection = FlexDirection.Row;
                refresh = new Button(Reload) { text = "Refresh models" };
                test = new Button(Test) { text = "Test local AI" };
                foreach (var button in new[] { refresh, test }) { button.AddToClassList("reset-button"); button.style.marginRight = 10; actions.Add(button); }
                content.Add(actions);
                status = AddHelp(statusText);
                var contexts = new List<string> { "2048", "4096", "8192", "16384", "32768", "65536", "131072", "262144" };
                if (!contexts.Contains(plugin.context.Value.ToString())) contexts.Add(plugin.context.Value.ToString());
                Choice("Context window (tokens)", contexts, plugin.context.Value.ToString(), value => { plugin.context.Value = int.Parse(value); if (plugin.output.Value >= plugin.context.Value) plugin.output.Value = Math.Min(2048, plugin.context.Value / 4); }, "256K = 262,144 total input and output tokens. Large contexts need more RAM/VRAM; model support varies.");
                Number("Maximum output tokens", plugin.output, 64, 262143, "Must be smaller than the context window. Includes thinking, where enabled.");
                Number("Temperature", plugin.temperature, 0, 2, "Lower values produce more consistent replies.");
                Number("Top P", plugin.topP, 0.01f, 1, "Nucleus sampling.");
                Number("Top K", plugin.topK, 0, 1000, "0 disables top-k sampling.");
                Number("Repeat penalty", plugin.repeatPenalty, 0, 2, "1 disables the repetition penalty.");
                Number("Seed", plugin.seed, -1, int.MaxValue, "-1 chooses a random seed.");
                Choice("Thinking", new List<string> { "Off", "On", "Model default" }, plugin.thinking.Value, value => plugin.thinking.Value = value, "Off is faster for dialogue. If your model rejects this option, use Model default.");
                Choice("Keep model loaded", new List<string> { "0", "5m", "10m", "30m", "1h", "-1" }, plugin.keepAlive.Value, value => plugin.keepAlive.Value = value, "0 unloads after each request; -1 keeps it loaded until Ollama exits.");
                Number("Request timeout (seconds)", plugin.timeout, 10, 1800, "Allow time for model loading and generation.");
                var grammar = new Toggle("Use structured JSON output") { value = plugin.structured.Value };
                grammar.RegisterValueChangedCallback(evt => { if (HostAiSettings.CanEdit) plugin.structured.Value = evt.newValue; });
                content.Add(grammar);
                AddHelp("Structured output uses the game's response schema. Turn it off only for models that reject JSON grammars. All settings save automatically.");
                Refresh();
            }

            private VisualElement Row(string label, string help)
            {
                var row = new VisualElement(); row.AddToClassList("openrouter-tuning-row");
                var title = new Label(label); title.AddToClassList("setting-label"); row.Add(title);
                if (!string.IsNullOrEmpty(help)) { var hint = new Label(help); hint.AddToClassList("setting-help"); hint.style.whiteSpace = WhiteSpace.Normal; row.Add(hint); }
                content.Add(row); return row;
            }
            private Label AddHelp(string text)
            {
                var label = new Label(text); label.AddToClassList("setting-help"); label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginTop = 8; label.style.marginBottom = 8; content.Add(label); return label;
            }
            private void Text(string label, ConfigEntry<string> entry, string help)
            {
                var field = new TextField { value = entry.Value, isDelayed = false }; field.AddToClassList("byok-field"); Row(label, help).Add(field);
                field.RegisterValueChangedCallback(evt =>
                {
                    if (!HostAiSettings.CanEdit) return;
                    // Persist the user's edit before validation. An unfinished
                    // or rejected URL must never silently select another server.
                    entry.Value = (evt.newValue ?? "").Trim();
                    attempted = true;
                    try { new GenerationSettings { Endpoint = entry.Value }.ValidatedEndpoint(); statusText = "Address saved. Click Refresh models to connect."; }
                    catch (Exception e) { statusText = e.Message; }
                    if (status != null) status.text = statusText;
                });
            }
            private void Choice(string label, List<string> choices, string value, Action<string> save, string help)
            {
                var field = new DropdownField { choices = choices }; field.SetValueWithoutNotify(value); field.AddToClassList("setting-dropdown"); Row(label, help).Add(field);
                field.RegisterValueChangedCallback(evt => { if (HostAiSettings.CanEdit) { save(evt.newValue); Refresh(); } });
            }
            private void Number(string label, ConfigEntry<int> entry, int min, int max, string help)
            {
                var field = new IntegerField { value = entry.Value, isDelayed = true }; field.AddToClassList("byok-field"); Row(label, help).Add(field);
                field.RegisterValueChangedCallback(evt =>
                {
                    if (!HostAiSettings.CanEdit) return;
                    int limit = entry == plugin.output ? Math.Min(max, plugin.context.Value - 1) : max;
                    entry.Value = Math.Max(min, Math.Min(limit, evt.newValue)); field.SetValueWithoutNotify(entry.Value); Refresh();
                });
                // Keep output display synchronized when a smaller context is selected.
                if (entry == plugin.output) content.schedule.Execute(() => { if (!disposed && field.value != entry.Value) field.SetValueWithoutNotify(entry.Value); }).Every(500);
            }
            private void Number(string label, ConfigEntry<float> entry, float min, float max, string help)
            {
                var field = new FloatField { value = entry.Value, isDelayed = true }; field.AddToClassList("byok-field"); Row(label, help).Add(field);
                field.RegisterValueChangedCallback(evt => { if (HostAiSettings.CanEdit) { entry.Value = float.IsNaN(evt.newValue) ? entry.DefaultValue is float ? (float)entry.DefaultValue : min : Mathf.Clamp(evt.newValue, min, max); field.SetValueWithoutNotify(entry.Value); } });
            }

            internal void Refresh()
            {
                if (disposed) return;
                int provider = PlayerPrefs.GetInt("HostAI.Backend", 0);
                backend.SetValueWithoutNotify(provider == 2 ? ProviderLabel : provider == 1 ? "OpenRouter (Bring Your Own Key)" : "Official Backend");
                content.style.display = Selected && HostAiSettings.CanEdit ? DisplayStyle.Flex : DisplayStyle.None;
                content.SetEnabled(HostAiSettings.CanEdit);
                if (status != null) status.text = statusText;
                if (refresh != null) refresh.SetEnabled(!busy);
                if (test != null) test.SetEnabled(!busy && !string.IsNullOrEmpty(plugin.model.Value));
                if (Selected && HostAiSettings.CanEdit && !attempted && !busy && hostScreen.ClassListContains("settings-screen--active")) Reload();
            }

            private async void Reload()
            {
                if (busy || disposed) return;
                string configuredAddress = plugin.endpoint.Value;
                attempted = true; busy = true; statusText = "Connecting to Ollama..."; Refresh();
                try
                {
                    string address = plugin.Snapshot().ValidatedEndpoint();
                    var tags = await Http(address + "/api/tags", null, 10, lifetime.Token);
                    var names = OllamaProtocol.LocalModels(tags);
                    if (disposed || plugin.endpoint.Value != configuredAddress) return;
                    if (names.Length == 0)
                    {
                        models.choices = new List<string> { "No local models installed" };
                        models.SetValueWithoutNotify(models.choices[0]);
                        statusText = "Ollama is running, but has no local models. Install a text/chat model in Ollama, then refresh.";
                    }
                    else
                    {
                        models.choices = names.ToList();
                        if (!names.Contains(plugin.model.Value)) plugin.model.Value = names[0];
                        models.SetValueWithoutNotify(plugin.model.Value);
                        statusText = names.Length + " local models found. Selected " + plugin.model.Value + ".";
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { if (plugin.endpoint.Value == configuredAddress) { statusText = e.Message; Error(e); } }
                finally { busy = false; if (!disposed) Refresh(); }
            }

            private async void Test()
            {
                if (busy || disposed) return;
                busy = true; statusText = "Testing local generation (the first load may take a while)..."; Refresh();
                try
                {
                    var settings = plugin.Snapshot();
                    settings.OutputTokens = Math.Min(settings.OutputTokens, 128);
                    var source = new JObject
                    {
                        ["messages"] = new JArray(new JObject { ["role"] = "user", ["content"] = "Reply with a JSON object containing ok set to true." }),
                        ["response_format"] = new JObject { ["type"] = "json_schema", ["json_schema"] = new JObject { ["schema"] = new JObject { ["type"] = "object", ["properties"] = new JObject { ["ok"] = new JObject { ["type"] = "boolean" } }, ["required"] = new JArray("ok") } } }
                    };
                    var result = await Http(settings.ValidatedEndpoint() + "/api/chat", OllamaProtocol.BuildRequest(source, settings), settings.TimeoutSeconds, lifetime.Token);
                    var wrapped = OllamaProtocol.WrapResponse(result, settings.Model);
                    bool valid = (bool?)JObject.Parse((string)wrapped["choices"][0]["message"]["content"])["ok"] == true;
                    if (!valid) throw new InvalidOperationException("The model replied but did not pass the JSON test. Try another model or generation settings.");
                    statusText = "Local AI test passed with " + settings.Model + "."; Log(statusText);
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { statusText = e.Message; Error(e); }
                finally { busy = false; if (!disposed) Refresh(); }
            }

            public void Dispose() { if (!disposed) { disposed = true; lifetime.Cancel(); lifetime.Dispose(); content.RemoveFromHierarchy(); } }
        }

        internal static readonly ConditionalWeakTable<HostAiSettingsPanel, LocalPanel> Panels = new ConditionalWeakTable<HostAiSettingsPanel, LocalPanel>();
    }
    public static class SetProvider
    {
        public static bool Prefix(int backend, ref bool __result)
        {
            if (backend != 2) return true;
            __result = HostAiSettings.CanEdit;
            if (__result) { PlayerPrefs.SetInt("HostAI.Backend", 2); PlayerPrefs.Save(); if (ScammerGameManager.Instance != null) ScammerGameManager.Instance.SetOfficialAiBackend(false); }
            return false;
        }
    }
    public static class DirectSelected { public static void Postfix(ref bool __result) { if (Plugin.Selected) __result = true; } }
    public static class DirectRoute { public static void Postfix(ref bool __result) { if (Plugin.Active) __result = true; } }
    public static class ResolveModel { public static void Postfix(ref string __result) { if (Plugin.Active) __result = Plugin.Instance.Snapshot().Model; } }
    public static class CreatePanel
    {
        public static void Postfix(HostAiSettingsPanel __instance, VisualElement root) { Plugin.Panels.Add(__instance, new Plugin.LocalPanel(root)); }
    }
    public static class RefreshPanel
    {
        public static void Postfix(HostAiSettingsPanel __instance) { Plugin.LocalPanel panel; if (Plugin.Panels.TryGetValue(__instance, out panel)) panel.Refresh(); }
    }
    public static class DisposePanel
    {
        public static void Postfix(HostAiSettingsPanel __instance) { Plugin.LocalPanel panel; if (Plugin.Panels.TryGetValue(__instance, out panel)) { panel.Dispose(); Plugin.Panels.Remove(__instance); } }
    }
    public static class LocalCompletion
    {
        public static bool Prefix(JObject body, CancellationToken cancellationToken, ref UniTask<JObject> __result)
        {
            if (!Plugin.Active) return true;
            __result = Plugin.Complete(body, cancellationToken).AsUniTask(); return false;
        }
    }

    // Force local prompt freezing even if a conversation was opened before switching provider.
    public static class LocalPrompts
    {
        public static void Prefix(AIConversationTurnOptions options) { if (Plugin.Active && options != null) options.GameplayRoute = "openrouter"; }
    }
    public static class LocalDialogueSchema
    {
        public static void Postfix(AiRequestCategory category, ref JObject __result)
        {
            // This build omits the optional BYOK schema but includes the same caller
            // contract as official-system-prompt.schema.json. Read it locally.
            if (!Plugin.Active || category != AiRequestCategory.Dialogue || __result != null) return;
            string path = Path.Combine(Application.streamingAssetsPath, "kolkata-json/core-prompts/official-system-prompt.schema.json");
            if (File.Exists(path)) __result = JObject.Parse(File.ReadAllText(path));
        }
    }

    public static class LocalGameEntry
    {
        // This changes local game entry only. The cloud service's authentication
        // status and Steam's networking/ownership checks remain authoritative.
        public static void Postfix(ref bool result)
        {
            if (Plugin.Selected) result = true;
        }
    }

    public static class LocalMenuPlay
    {
        private static readonly MethodInfo ShowScreen = typeof(MainMenuToolkitController).GetMethod("ShowScreen", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Prefix(MainMenuToolkitController controller)
        {
            var service = BackendAuthenticationStatusSource.Current;
            if (!Plugin.Selected && service != null && service.CanStartGame) return true;
            // Host settings can only be edited after entering a lobby. Make the
            // local route reachable when cloud sign-in is unavailable at startup.
            PlayerPrefs.SetInt("HostAI.Backend", 2);
            PlayerPrefs.Save();
            Action play = () => ShowScreen.Invoke(controller, new object[] { "multiplayer-screen", "MULTIPLAYER" });
            if (!MainMenuToolkitController.TryDeferForQwen(play)) play();
            return false;
        }
    }

    public static class LocalMenuLayout
    {
        private static readonly ConditionalWeakTable<VisualElement, Menu> Menus = new ConditionalWeakTable<VisualElement, Menu>();

        public static void Postfix(VisualElement root)
        {
            if (root == null) return;
            Menu menu;
            if (!Menus.TryGetValue(root, out menu))
            {
                var play = root.Q<Button>("play-button");
                var quit = root.Q<Button>("quit-button");
                if (play == null || quit == null || quit.parent == null) return;
                menu = new Menu(root, play, quit);
                Menus.Add(root, menu);
            }
            menu.Refresh();
        }

        private sealed class Menu
        {
            private readonly VisualElement root, menuRoot;
            private readonly Button play, signIn;

            public Menu(VisualElement root, Button play, Button quit)
            {
                this.root = root;
                this.play = play;
                menuRoot = root.Q<VisualElement>("menu-root");
                signIn = new Button(SignIn) { name = "ollama-service-sign-in" };
                signIn.AddToClassList("menu-button");
                quit.parent.Insert(quit.parent.IndexOf(quit) + 1, signIn);
            }

            private static void SignIn()
            {
                var service = BackendAuthenticationStatusSource.Current;
                if (service == null) return;
                if (service.BrowserSignInPending) service.CancelBrowserSignIn();
                else if (service.BrowserSignInAvailable) service.BeginBrowserSignIn();
                else service.RefreshAiCreditStatus(force: true);
            }

            public void Refresh()
            {
                var service = BackendAuthenticationStatusSource.Current;
                bool signedIn = service != null && service.CanStartGame;
                bool pending = service != null && service.BrowserSignInPending;
                menuRoot?.EnableInClassList("menu-root--auth-recovery", false);
                play.text = LanguageManager.GetMenuText("PLAY");
                play.SetEnabled(true);
                play.EnableInClassList("menu-button--primary", true);
                play.tooltip = Plugin.Selected || !signedIn ? "Play with Ollama. Configure the server and model in Settings > Host after hosting." : string.Empty;
                signIn.text = pending ? "CANCEL SIGN-IN" : "SIGN IN WITH STEAM";
                signIn.SetEnabled(service != null && (!signedIn || pending));
                signIn.tooltip = signedIn ? "Already signed in to the official AI service." : "Optional sign-in for the official AI service. Ollama does not need it.";
                if (!Plugin.Selected) return;
                var backend = root.Q<Label>("backend-auth-status");
                if (backend != null)
                {
                    backend.text = "AI BACKEND: OLLAMA SELECTED";
                    backend.EnableInClassList("connection-state--ready", false);
                    backend.EnableInClassList("connection-state--failed", false);
                }
                // Keep Steam connectivity warnings visible for multiplayer.
                var steam = root.Q<Label>("steam-connection-status");
                if (steam != null && steam.ClassListContains("connection-state--failed")) return;
                var title = root.Q<Label>("connection-warning-title");
                var body = root.Q<Label>("connection-warning-body");
                if (title != null) title.text = "LOCAL AI: OLLAMA";
                if (body != null)
                {
                    body.text = "AI-service sign-in is optional. Configure and test Ollama in Settings > Host after hosting.";
                    body.style.display = DisplayStyle.Flex;
                }
                var browser = root.Q<Label>("browser-sign-in-message");
                var support = root.Q<Label>("sign-in-support-message");
                if (!pending && browser != null) browser.style.display = DisplayStyle.None;
                if (support != null) support.style.display = DisplayStyle.None;
            }
        }
    }

    public static class LocalDialogueTokens
    {
        public static void Prefix(CallerDialogueSession.Turn turn, JObject response)
        {
            if (Plugin.Active && DialogueTokenRepair.Apply(response, turn.Available, turn.Hidden))
                Plugin.Log("Restored missing brackets around an available dialogue token.");
        }
    }

        public static class LocalTransportBudget
    {
        private static readonly MethodInfo Estimate = typeof(AIConversation).GetMethod("EstimateInput", BindingFlags.Instance | BindingFlags.NonPublic);
        public static bool Prefix(AIConversation __instance, object pending, ref bool __result)
        {
            if (!Plugin.Active) return true;
            __result = (long)Estimate.Invoke(__instance, new[] { pending }) < Plugin.InputBudget(); return false;
        }
    }
}
