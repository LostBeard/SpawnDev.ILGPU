using System;
using System.Collections.Generic;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Measures the per-invocation storage of the arrays a WGSL module declares in the <c>function</c> and <c>private</c>
    /// address spaces (<c>var a : array&lt;T, N&gt;;</c> inside a function, or <c>var&lt;private&gt;</c> at module scope).
    /// </summary>
    /// <remarks>
    /// <para>
    /// WORKAROUND support for https://github.com/LostBeard/SpawnDev.ILGPU/issues/10 (upstream:
    /// https://issues.chromium.org/issues/585125915). On an NVIDIA RTX 4070 (Chrome, D3D12), a kernel holding more than
    /// 6608 bytes of such arrays per thread, followed by another dispatch in the same submit, leaves the device to die
    /// on a later large kernel with DXGI_ERROR_DEVICE_HUNG. The dispatch path ends the batch after such a kernel
    /// (<see cref="WebGPUBackend.NvidiaLargeFunctionArrayFlush"/>).
    /// </para>
    /// <para>
    /// The measure is taken from the FINISHED WGSL, not from the code generator, so it covers every way an array reaches
    /// the shader (local allocas, <c>new T[N]</c>, hoisted copies, helpers) and holds for kernels rebuilt from the shader
    /// artifact cache. It is an UPPER bound in the direction that matters: every declaration counts once per occurrence,
    /// elements narrower than 4 bytes count as 4, <c>vec3</c> counts as 4 lanes, and a type it cannot size counts as 16
    /// bytes per element.
    /// </para>
    /// </remarks>
    public static class WgslFunctionArrays
    {
        /// <summary>Bytes of every <c>function</c> / <c>private</c> address-space array declared in <paramref name="wgsl"/>.</summary>
        public static long EstimateBytes(string wgsl)
        {
            if (string.IsNullOrEmpty(wgsl)) return 0;
            var code = StripComments(wgsl);
            var structs = ParseStructs(code);
            long total = 0;
            int i = 0;
            while ((i = code.IndexOf("var", i, StringComparison.Ordinal)) >= 0)
            {
                int j = i + 3;
                bool wordStart = i == 0 || !IsIdent(code[i - 1]);
                if (!wordStart || j >= code.Length) { i = j; continue; }
                if (code[j] == '<')
                {
                    int close = code.IndexOf('>', j);
                    if (close < 0) break;
                    var space = code.Substring(j + 1, close - j - 1).Trim();
                    // var<storage, ...>, var<uniform>, var<workgroup> are not per-invocation storage
                    if (space != "private" && space != "function") { i = close + 1; continue; }
                    j = close + 1;
                }
                else if (IsIdent(code[j])) { i = j; continue; }   // an identifier such as "variable"

                j = SkipSpace(code, j);
                int nameStart = j;
                while (j < code.Length && IsIdent(code[j])) j++;
                if (j == nameStart) { i = j; continue; }
                j = SkipSpace(code, j);
                if (j >= code.Length || code[j] != ':') { i = j; continue; }   // `var x = ...` without a type
                j = SkipSpace(code, j + 1);
                if (string.CompareOrdinal(code, j, "array", 0, 5) == 0)
                {
                    int end = TypeEnd(code, j);
                    total += TypeBytes(code.Substring(j, end - j), structs, 0);
                    i = end;
                }
                else i = j;
            }
            return total;
        }

        private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static int SkipSpace(string s, int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            return i;
        }

        /// <summary>End (exclusive) of the type starting at <paramref name="start"/>: an identifier plus a balanced
        /// <c>&lt;...&gt;</c> argument list when one follows.</summary>
        private static int TypeEnd(string s, int start)
        {
            int i = start;
            while (i < s.Length && IsIdent(s[i])) i++;
            i = SkipSpace(s, i);
            if (i >= s.Length || s[i] != '<') return i;
            int depth = 0;
            for (; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>' && --depth == 0) return i + 1;
            }
            return s.Length;
        }

        private static string StripComments(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    if (i < s.Length) sb.Append('\n');
                }
                else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? s.Length : end + 1;
                    sb.Append(' ');
                }
                else sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>Member type texts of every <c>struct Name { ... }</c> in the module.</summary>
        private static Dictionary<string, List<string>> ParseStructs(string code)
        {
            var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            int i = 0;
            while ((i = code.IndexOf("struct", i, StringComparison.Ordinal)) >= 0)
            {
                int j = i + 6;
                if ((i > 0 && IsIdent(code[i - 1])) || j >= code.Length || IsIdent(code[j])) { i = j; continue; }
                j = SkipSpace(code, j);
                int nameStart = j;
                while (j < code.Length && IsIdent(code[j])) j++;
                var name = code.Substring(nameStart, j - nameStart);
                j = SkipSpace(code, j);
                if (name.Length == 0 || j >= code.Length || code[j] != '{') { i = j; continue; }
                int close = code.IndexOf('}', j);
                if (close < 0) break;
                var members = new List<string>();
                foreach (var member in SplitTopLevel(code.Substring(j + 1, close - j - 1)))
                {
                    int colon = member.IndexOf(':');
                    if (colon >= 0) members.Add(member.Substring(colon + 1).Trim());
                }
                result[name] = members;
                i = close + 1;
            }
            return result;
        }

        /// <summary>Splits on ',' and ';' outside any <c>&lt;...&gt;</c>.</summary>
        private static List<string> SplitTopLevel(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if ((c == ',' || c == ';') && depth == 0)
                {
                    parts.Add(s.Substring(start, i - start));
                    start = i + 1;
                }
            }
            if (start < s.Length) parts.Add(s.Substring(start));
            return parts;
        }

        private const long UnknownElementBytes = 16;
        private const long UnknownLengthBytes = 1L << 20;

        private static long TypeBytes(string type, Dictionary<string, List<string>> structs, int depth)
        {
            type = type.Trim();
            if (depth > 16) return UnknownElementBytes;
            int lt = type.IndexOf('<');
            string head = (lt < 0 ? type : type.Substring(0, lt)).Trim();
            string args = lt < 0 ? "" : type.Substring(lt + 1, Math.Max(0, type.LastIndexOf('>') - lt - 1));
            switch (head)
            {
                case "i32": case "u32": case "f32": case "bool": case "f16": case "atomic":
                    return 4;
                case "array":
                {
                    var parts = SplitTopLevel(args);
                    if (parts.Count < 2) return 0;   // runtime-sized: not a function/private array
                    var count = parts[parts.Count - 1].Trim().TrimEnd('u', 'i');
                    // A length that is not a literal (an override or a const name) cannot be sized here: count it as
                    // large, so the kernel is treated as over any threshold rather than silently under it.
                    if (!long.TryParse(count, out long n)) return UnknownLengthBytes;
                    return n * TypeBytes(string.Join(",", parts.GetRange(0, parts.Count - 1)), structs, depth + 1);
                }
            }
            if (head.StartsWith("vec", StringComparison.Ordinal) && head.Length >= 4 && char.IsDigit(head[3]))
            {
                int lanes = head[3] - '0';
                return (lanes == 3 ? 4 : lanes) * 4;
            }
            if (head.StartsWith("mat", StringComparison.Ordinal) && head.Length >= 6 && char.IsDigit(head[3]) && char.IsDigit(head[5]))
            {
                int cols = head[3] - '0', rows = head[5] - '0';
                return cols * (rows == 3 ? 4 : rows) * 4;
            }
            if (structs.TryGetValue(head, out var members))
            {
                long sum = 0;
                foreach (var m in members) sum += TypeBytes(m, structs, depth + 1);
                return sum > 0 ? sum : 4;
            }
            return UnknownElementBytes;
        }
    }
}
