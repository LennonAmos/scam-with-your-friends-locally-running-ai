using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ScamOllama
{
    // Small local models sometimes drop the brackets around game control tokens.
    // Only repair tokens the current game turn already permits. The original
    // NormalizeReply, disclosure processing and code generator still run afterward.
    public static class DialogueTokenRepair
    {
        private static readonly Regex Token = new Regex(
            @"(?<![\w\[])\[{0,2}(?<tag>FICTIONAL_[A-Z0-9_]+)\]{0,2}(?![\w\]])",
            RegexOptions.CultureInvariant);

        public static bool Apply(JObject response, IEnumerable<string> available, bool hidden)
        {
            if (hidden || response == null || response["dialogue"]?.Type != JTokenType.String || available == null) return false;
            var allowed = new HashSet<string>(available, System.StringComparer.Ordinal);
            string before = (string)response["dialogue"];
            string after = Token.Replace(before, match => allowed.Contains(match.Groups["tag"].Value)
                ? "[[" + match.Groups["tag"].Value + "]]" : match.Value);
            if (before == after) return false;
            response["dialogue"] = after;
            return true;
        }
    }
}
