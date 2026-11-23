using System.Text;
using System.Text.RegularExpressions;

namespace SpawnDev.ILGPU
{
    /// <summary>
    /// Makes a kernel's finished shader text deterministic: the same kernel compiled again gives byte-identical text
    /// (5.4.46). Used for WGSL (WebGPU) and GLSL (WebGL).
    /// <para>
    /// The generators name things after IR node ids, which come from counters shared by everything a context compiles. WGSL:
    /// helper functions <c>Name_{method.Id}</c>, labels <c>L_{method.Id}_n</c>, helper parameters <c>p_{param.Id}</c>,
    /// structs <c>struct_{type.Id}</c>, and IR block ids in comments. GLSL: <c>struct_{type.Id}</c> and helper functions
    /// <c>fn_{Name}_{method.Id}</c>. So the text of one kernel depended on what the context compiled before it. The text
    /// keys the WebGPU compiled-shader cache (and the browser's pipeline cache) and now the WebGL program id, so a kernel
    /// compiled again (its owner collected, a model switched back) missed them: a duplicate pipeline or GL program was kept
    /// and the browser compiled the shader again (ML 2026-11-23: an LLM switched away and back took the WebGPU shader cache
    /// from 57 to 75 shaders).
    /// </para>
    /// <para>
    /// Every identifier with a digits-only segment (<c>Name_123</c>, <c>Name_123_m0</c>, <c>L_57_3</c>) is renamed:
    /// identifiers of the same shape (digit segments masked: <c>Name_#_m0</c>) are numbered in order of first appearance,
    /// the first masked segment carrying the number and any further ones becoming 0. It is a renaming, not a rewrite: two
    /// shapes differ outside their masked segments and each shape gets its own range, so two names never collide. An
    /// optional filter limits the renaming to some shapes (GLSL: only the id-based ones, because the WebGL host builds
    /// uniform paths such as <c>field_{n}</c> from its own counters). Comments get the same renaming, and IR block
    /// references in them (<c>B124</c>) become <c>B#</c>. Names the host refers to go through <see cref="Rename"/>.
    /// </para>
    /// </summary>
    internal sealed class ShaderCanonicalNames
    {
        static readonly Regex s_identifier = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex s_digitSegment = new(@"_\d+(?=_|$)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex s_commentBlockReference = new(@"\b([A-Z]{1,2})(\d+)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        readonly Dictionary<string, string> _renames = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> _nextIndex = new(StringComparer.Ordinal);
        readonly Func<string, bool>? _renameShape;
        readonly MatchEvaluator _map;

        ShaderCanonicalNames(Func<string, bool>? renameShape)
        {
            _renameShape = renameShape;
            _map = Map;
        }

        /// <summary>Canonicalizes <paramref name="source"/>; the returned instance renames host-side names the same way.</summary>
        /// <param name="source">The finished shader text (C-style comments).</param>
        /// <param name="names">The renaming that was applied.</param>
        /// <param name="renameShape">Which identifier shapes (digit segments masked as <c>_#</c>) to rename; null = all.</param>
        public static string Canonicalize(string source, out ShaderCanonicalNames names, Func<string, bool>? renameShape = null)
        {
            names = new ShaderCanonicalNames(renameShape);
            var sb = new StringBuilder(source.Length);
            int i = 0;
            while (i < source.Length)
            {
                int line = source.IndexOf("//", i, StringComparison.Ordinal);
                int block = source.IndexOf("/*", i, StringComparison.Ordinal);
                int start = line < 0 ? block : block < 0 ? line : Math.Min(line, block);
                if (start < 0)
                {
                    sb.Append(names.RenameIn(source.Substring(i)));
                    break;
                }
                sb.Append(names.RenameIn(source.Substring(i, start - i)));
                int end;
                if (start == line)
                {
                    end = source.IndexOf('\n', start);
                    if (end < 0) end = source.Length;
                }
                else
                {
                    end = source.IndexOf("*/", start + 2, StringComparison.Ordinal);
                    end = end < 0 ? source.Length : end + 2;
                }
                sb.Append(names.RenameInComment(source.Substring(start, end - start)));
                i = end;
            }
            return sb.ToString();
        }

        /// <summary>The canonical name of a generated identifier (unchanged when it has no digits-only segment).</summary>
        public string Rename(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return s_identifier.Replace(name, _map);
        }

        string RenameIn(string code) => s_identifier.Replace(code, _map);

        string RenameInComment(string comment) =>
            s_commentBlockReference.Replace(s_identifier.Replace(comment, _map), m => m.Groups[1].Value + "#");

        string Map(Match m)
        {
            var original = m.Value;
            if (_renames.TryGetValue(original, out var renamed)) return renamed;
            if (!s_digitSegment.IsMatch(original)) return original;
            var shape = s_digitSegment.Replace(original, "_#");
            if (_renameShape != null && !_renameShape(shape))
            {
                _renames[original] = original;
                return original;
            }
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
