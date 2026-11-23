using System.Text;
using System.Text.RegularExpressions;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Makes a kernel's finished WGSL deterministic: the same kernel compiled again gives byte-identical text.
    /// <para>
    /// The generators name things after IR node ids, which come from counters shared by everything a context compiles: helper
    /// functions <c>Name_{method.Id}</c>, labels <c>L_{method.Id}_n</c>, helper parameters <c>p_{param.Id}</c>, structs
    /// <c>struct_{type.Id}</c>, and IR block ids in comments. So the WGSL of one kernel depended on what the context compiled
    /// before it. The text is the key of the accelerator's compiled-shader cache (and of the browser's own pipeline cache), so a
    /// kernel compiled again (its owner collected, or a model switched back) missed it: a duplicate pipeline was kept and D3D
    /// compiled the shader again (Tuvok 2026-11-23: an LLM reload took the cache from 57 to 75 shaders; Geordi: 6-17 s to
    /// re-create a codec).
    /// </para>
    /// <para>
    /// Every identifier with a digits-only segment (<c>Name_123</c>, <c>Name_123_m0</c>, <c>L_57_3</c>) is renamed: identifiers
    /// of the same shape (digit segments masked) are numbered in order of first appearance. It is a renaming, not a rewrite:
    /// every such identifier is renamed and nothing else is, and each shape gets its own range, so two names never collide. Comments get the same renaming, and the remaining IR block
    /// references in them (<c>B124</c>) become <c>B#</c>. Names the host refers to (dynamic shared-memory override constants,
    /// coalesce bindings, struct type names in the scalar-packing manifest) go through <see cref="Rename"/> as well.
    /// </para>
    /// </summary>
    internal sealed class WGSLCanonicalNames
    {
        // an identifier with at least one digits-only segment (_123), anywhere in it: Name_1063214, Name_1063214_m0, L_57_3
        static readonly Regex s_numberedIdentifier = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex s_digitSegment = new(@"_\d+(?=_|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex s_commentBlockReference = new(@"\b([A-Z]{1,2})(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        readonly Dictionary<string, string> _renames = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _nextIndex = new(StringComparer.Ordinal);

        WGSLCanonicalNames() { }

        /// <summary>Canonicalizes <paramref name="wgsl"/>; the returned instance renames host-side names the same way.</summary>
        public static string Canonicalize(string wgsl, out WGSLCanonicalNames names)
        {
            names = new WGSLCanonicalNames();
            var sb = new StringBuilder(wgsl.Length);
            int i = 0;
            while (i < wgsl.Length)
            {
                int line = wgsl.IndexOf("//", i, StringComparison.Ordinal);
                int block = wgsl.IndexOf("/*", i, StringComparison.Ordinal);
                int start = line < 0 ? block : block < 0 ? line : Math.Min(line, block);
                if (start < 0)
                {
                    sb.Append(names.RenameIn(wgsl.Substring(i)));
                    break;
                }
                sb.Append(names.RenameIn(wgsl.Substring(i, start - i)));
                int end;
                if (start == line)
                {
                    end = wgsl.IndexOf('\n', start);
                    if (end < 0) end = wgsl.Length;
                }
                else
                {
                    end = wgsl.IndexOf("*/", start + 2, StringComparison.Ordinal);
                    end = end < 0 ? wgsl.Length : end + 2;
                }
                sb.Append(names.RenameInComment(wgsl.Substring(start, end - start)));
                i = end;
            }
            return sb.ToString();
        }

        /// <summary>The canonical name of a generated identifier (unchanged when it is not of the form prefix_N).</summary>
        public string Rename(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return s_numberedIdentifier.Replace(name, Map);
        }

        string RenameIn(string code) => s_numberedIdentifier.Replace(code, Map);

        string RenameInComment(string comment) =>
            s_commentBlockReference.Replace(s_numberedIdentifier.Replace(comment, Map), m => m.Groups[1].Value + "#");

        // The identifier's SHAPE is the identifier with each digits-only segment masked (Name_#_m0, L_#_#); identifiers of the
        // same shape are numbered 0, 1, 2... in order of first appearance: the first masked segment carries the number, any
        // further ones become 0. Every identifier with a digit segment is renamed and nothing else is, and two shapes never
        // produce the same text (they differ outside the masked segments), so the renaming cannot collide.
        string Map(Match m)
        {
            var original = m.Value;
            if (!s_digitSegment.IsMatch(original)) return original;
            if (_renames.TryGetValue(original, out var renamed)) return renamed;
            var shape = s_digitSegment.Replace(original, "_#");
            _nextIndex.TryGetValue(shape, out int k);
            _nextIndex[shape] = k + 1;
            var number = k.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int first = shape.IndexOf('#');
            renamed = shape.Substring(0, first) + number + shape.Substring(first + 1).Replace("#", "0");
            _renames[original] = renamed;
            return renamed;
        }
    }
}
