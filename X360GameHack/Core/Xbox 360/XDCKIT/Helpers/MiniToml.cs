// =============================================================================
// XDCKIT.MiniToml.cs - Minimal TOML scalar / list helpers
// =============================================================================
// XDCKIT only needs to parse the very small subset of TOML used by the Xenia
// Canary game-patches repository (see Helpers/XeniaPatch.cs).  The patches
// only use:
//
//   * scalar literals (strings, integers - decimal or 0x hex, floats, bools)
//   * single-element string lists (hash = "X")
//   * multi-element string lists (hash = [ "A", "B" ])
//   * `#` line comments (anywhere a value can appear, until end of line)
//
// Rather than pulling in a full TOML library on .NET Framework 4.8, this file
// exposes the parsing primitives the loader needs.  Reuse in your own code is
// supported but it is not a full TOML implementation - do not feed it
// arbitrary `.toml` files outside the Xenia patches surface.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

    /// <summary>Scalar / list parsing helpers for the Xenia patch TOML subset.</summary>
    public static class MiniToml
    {
        /// <summary>Strip a trailing <c>#</c> line-comment, respecting string quotes.</summary>
        public static string StripComment(string line)
        {
            if (string.IsNullOrEmpty(line)) return line ?? string.Empty;
            bool inString = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\\' && i + 1 < line.Length) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (c == '#' && !inString) return line.Substring(0, i);
            }
            return line;
        }

        /// <summary>Parse a scalar string token (with optional surrounding quotes).</summary>
        public static string ParseScalarString(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            string t = StripComment(token).Trim();
            if (t.Length == 0) return string.Empty;
            if (t.Length >= 2 && t[0] == '"' && t[t.Length - 1] == '"')
            {
                var inner = t.Substring(1, t.Length - 2);
                return Unescape(inner);
            }
            // Unquoted bareword fallback (TOML doesn't allow this for value strings,
            // but Xenia files sometimes inline literal hex/title-ids without quotes).
            return t;
        }

        /// <summary>Parse a scalar integer.  Accepts plain decimal and <c>0x...</c> hex.</summary>
        public static long ParseScalarLong(string token)
        {
            if (string.IsNullOrEmpty(token)) return 0;
            string t = StripComment(token).Trim();
            if (t.Length == 0) return 0;

            // Strip optional trailing comment / whitespace.
            int end = 0;
            while (end < t.Length && (char.IsLetterOrDigit(t[end]) || t[end] == 'x' || t[end] == 'X' ||
                                       t[end] == '-' || t[end] == '+'))
                end++;
            t = t.Substring(0, end);
            if (t.Length == 0) return 0;

            int sign = 1;
            if (t[0] == '+') t = t.Substring(1);
            else if (t[0] == '-') { sign = -1; t = t.Substring(1); }

            if (t.Length >= 2 && t[0] == '0' && (t[1] == 'x' || t[1] == 'X'))
            {
                if (ulong.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                    return sign * unchecked((long)hex);
                return 0;
            }
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dec))
                return sign * dec;
            return 0;
        }

        /// <summary>Parse a scalar floating-point value.</summary>
        public static double ParseScalarDouble(string token)
        {
            if (string.IsNullOrEmpty(token)) return 0;
            string t = StripComment(token).Trim();
            if (t.Length == 0) return 0;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
            // Allow integers in places that want a float ("value = 1" instead of "1.0").
            if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                return i;
            return 0;
        }

        /// <summary>Parse a scalar boolean (<c>true</c>/<c>false</c>, case-insensitive).</summary>
        public static bool ParseScalarBool(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;
            string t = StripComment(token).Trim().ToLowerInvariant();
            return t == "true" || t == "1" || t == "yes";
        }

        /// <summary>
        /// Parse a string-or-list value as a list of strings.  Accepts both
        /// <c>"single"</c> and <c>[ "a", "b" ]</c> forms.  Whitespace and
        /// comments inside the array are ignored.
        /// </summary>
        public static List<string> ParseStringList(string token)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(token)) return list;
            string t = StripComment(token).Trim();
            if (t.Length == 0) return list;

            if (t[0] != '[')
            {
                string single = ParseScalarString(t);
                if (!string.IsNullOrEmpty(single)) list.Add(single);
                return list;
            }

            int end = t.LastIndexOf(']');
            if (end < 0) end = t.Length;
            string body = t.Substring(1, Math.Max(0, end - 1));

            // Split on commas that aren't inside a quoted string.
            int start = 0;
            bool inStr = false;
            for (int i = 0; i <= body.Length; i++)
            {
                bool atEnd = i == body.Length;
                char c = atEnd ? ',' : body[i];
                if (!atEnd)
                {
                    if (c == '\\' && i + 1 < body.Length) { i++; continue; }
                    if (c == '"') { inStr = !inStr; continue; }
                    if (c != ',' || inStr) continue;
                }
                string elem = body.Substring(start, i - start).Trim();
                if (elem.Length > 0)
                {
                    string parsed = ParseScalarString(elem);
                    if (!string.IsNullOrEmpty(parsed)) list.Add(parsed);
                }
                start = i + 1;
            }
            return list;
        }

        /// <summary>Unescape the standard TOML basic-string escapes (<c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\r</c>, <c>\t</c>).</summary>
        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char next = s[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  i++; continue;
                        case '\\': sb.Append('\\'); i++; continue;
                        case 'n':  sb.Append('\n'); i++; continue;
                        case 'r':  sb.Append('\r'); i++; continue;
                        case 't':  sb.Append('\t'); i++; continue;
                        case '0':  sb.Append('\0'); i++; continue;
                    }
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
