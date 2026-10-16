using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

// Offline patching avoids runtime detours and does not change any core libraries.
internal static class Patcher
{
    private static ModuleDefinition hooks;
    private static ModuleDefinition main;
    private static ModuleDefinition scripts;
    private static int changes;

    private static MethodDefinition Method(ModuleDefinition module, string type, string name)
    { return module.GetType(type).Methods.Single(m => m.Name == name); }
    private static MethodReference Hook(ModuleDefinition module, string type, string name)
    { return module.ImportReference(Method(hooks, "ScamOllama." + type, name)); }
    private static MethodDefinition Prepare(ModuleDefinition module, string type, string name)
    {
        var method = Method(module, type, name);
        method.Body.SimplifyMacros();
        method.Body.InitLocals = true;
        changes++;
        return method;
    }
    private static void Before(ILProcessor il, Instruction anchor, params Instruction[] instructions)
    { foreach (var instruction in instructions) il.InsertBefore(anchor, instruction); }
    private static void AppendReturn(ModuleDefinition module, string type, string name, string hookType, int firstArgument = -1)
    {
        var method = Prepare(module, type, name);
        var il = method.Body.GetILProcessor();
        var hook = Hook(module, hookType, "Postfix");
        foreach (var ret in method.Body.Instructions.Where(i => i.OpCode == OpCodes.Ret).ToArray())
        {
            // Mutate the return instruction itself so branches targeting it also
            // run the callback. Append a new ret after the callback.
            var returnInstruction = Instruction.Create(OpCodes.Ret);
            il.InsertAfter(ret, returnInstruction);
            ret.OpCode = OpCodes.Nop;
            ret.Operand = null;
            if (method.ReturnType.MetadataType == MetadataType.Void)
            {
                il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldarg_0));
                if (firstArgument >= 0) il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldarg, method.Parameters[firstArgument]));
            }
            else
            {
                var result = new VariableDefinition(method.ReturnType);
                method.Body.Variables.Add(result);
                il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Stloc, result));
                if (firstArgument >= 0) il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldarg, method.Parameters[firstArgument]));
                il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldloca, result));
                il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Call, hook));
                il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Ldloc, result));
                continue;
            }
            il.InsertBefore(returnInstruction, Instruction.Create(OpCodes.Call, hook));
        }
    }
    private static void PrefixOverride(ModuleDefinition module, string type, string name, string hookType, int[] arguments, bool instance)
    {
        var method = Prepare(module, type, name);
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        var result = new VariableDefinition(method.ReturnType);
        method.Body.Variables.Add(result);
        if (instance) il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
        foreach (int arg in arguments) il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg, method.Parameters[arg]));
        Before(il, first, Instruction.Create(OpCodes.Ldloca, result), Instruction.Create(OpCodes.Call, Hook(module, hookType, "Prefix")),
            Instruction.Create(OpCodes.Brtrue, first), Instruction.Create(OpCodes.Ldloc, result), Instruction.Create(OpCodes.Ret));
    }

    public static int Main(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("Usage: Patcher.exe <game Managed folder> <ScamOllama.dll> <staging output folder>");
            string managed = Path.GetFullPath(args[0]);
            var resolver = new DefaultAssemblyResolver(); resolver.AddSearchDirectory(managed); resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
            var options = new ReaderParameters { AssemblyResolver = resolver, InMemory = true };
            main = ModuleDefinition.ReadModule(Path.Combine(managed, "Assembly-CSharp.dll"), options);
            scripts = ModuleDefinition.ReadModule(Path.Combine(managed, "ScriptsAssDef.dll"), options);
            hooks = ModuleDefinition.ReadModule(args[1], options);
            if (main.AssemblyReferences.Any(r => r.Name == "ScamOllama") || scripts.AssemblyReferences.Any(r => r.Name == "ScamOllama"))
                throw new InvalidOperationException("Input assemblies are already patched. Use the preserved original backups.");

            PrefixOverride(scripts, "HostAiSettings", "TrySetBackend", "SetProvider", new[] { 0 }, false);
            AppendReturn(scripts, "HostAiSettings", "get_DirectSelected", "DirectSelected");
            AppendReturn(scripts, "HostAiSettings", "get_UseDirectOpenRouter", "DirectRoute");
            AppendReturn(scripts, "HostAiSettings", "ResolveModel", "ResolveModel");
            AppendReturn(scripts, "HostAiSettingsPanel", ".ctor", "CreatePanel", 0);
            AppendReturn(scripts, "HostAiSettingsPanel", "Refresh", "RefreshPanel");
            AppendReturn(scripts, "HostAiSettingsPanel", "Dispose", "DisposePanel");
            PrefixOverride(main, "KolkataApi", "CompleteOpenRouterAsync", "LocalCompletion", new[] { 0, 1 }, false);
            var freeze = Prepare(main, "AIConversation", "FreezePromptAsync");
            Before(freeze.Body.GetILProcessor(), freeze.Body.Instructions[0], Instruction.Create(OpCodes.Ldarg, freeze.Parameters[0]), Instruction.Create(OpCodes.Call, Hook(main, "LocalPrompts", "Prefix")));
            AppendReturn(main, "GameplayPrompts", "ResponseFormat", "LocalDialogueSchema", 0);
            var normalize = Prepare(main, "CallerDialogueSession/Turn", "NormalizeReply");
            Before(normalize.Body.GetILProcessor(), normalize.Body.Instructions[0],
                Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg, normalize.Parameters[0]),
                Instruction.Create(OpCodes.Call, Hook(main, "LocalDialogueTokens", "Prefix")));
            PrefixOverride(main, "AIConversation", "FitsTransport", "LocalTransportBudget", new[] { 0 }, true);
            var compact = Prepare(main, "AIConversation", "CompactImmutableLocked");
            int tokens = 0, pairs = 0;
            foreach (var instruction in compact.Body.Instructions.ToArray())
            {
                bool wide = instruction.OpCode == OpCodes.Ldc_I8;
                if ((instruction.OpCode == OpCodes.Ldc_I4 || wide) && Convert.ToInt64(instruction.Operand) == 10000)
                {
                    instruction.OpCode = OpCodes.Call; instruction.Operand = Hook(main, "Plugin", "InputBudget");
                    if (wide) compact.Body.GetILProcessor().InsertAfter(instruction, Instruction.Create(OpCodes.Conv_I8));
                    tokens++;
                }
                else if (instruction.OpCode == OpCodes.Ldc_I4 && (int)instruction.Operand == 128)
                { instruction.OpCode = OpCodes.Call; instruction.Operand = Hook(main, "Plugin", "PairBudget"); pairs++; }
            }
            if (tokens != 3 || pairs != 2) throw new InvalidOperationException("Conversation budget layout changed: token constants=" + tokens + ", pair constants=" + pairs);
            Directory.CreateDirectory(args[2]);
            main.Write(Path.Combine(args[2], "Assembly-CSharp.dll"));
            scripts.Write(Path.Combine(args[2], "ScriptsAssDef.dll"));
            Console.WriteLine("Prepared " + changes + " method patches. Original runtime libraries were not changed.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
