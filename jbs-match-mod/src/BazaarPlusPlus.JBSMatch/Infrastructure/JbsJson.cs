#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BazaarPlusPlus.JBSMatch.Infrastructure;

internal static class JbsJson
{
    internal static bool TryParseObject(string json, out Dictionary<string, object?> obj)
    {
        obj = new Dictionary<string, object?>(StringComparer.Ordinal);
        try
        {
            var reader = new Reader(json);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.IsDone || value is not Dictionary<string, object?> parsed)
                return false;

            obj = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryParseArray(string json, out List<object?> array)
    {
        array = new List<object?>();
        try
        {
            var reader = new Reader(json);
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.IsDone || value is not List<object?> parsed)
                return false;

            array = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string? String(Dictionary<string, object?> obj, string key)
        => obj.TryGetValue(key, out var value) ? value as string : null;

    internal static int Int(Dictionary<string, object?> obj, string key, int fallback = 0)
    {
        if (!obj.TryGetValue(key, out var value) || value == null)
            return fallback;

        return value switch
        {
            int i => i,
            long l when l <= int.MaxValue && l >= int.MinValue => (int)l,
            double d when d <= int.MaxValue && d >= int.MinValue => (int)d,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback
        };
    }

    internal static bool Bool(Dictionary<string, object?> obj, string key, bool fallback = false)
    {
        if (!obj.TryGetValue(key, out var value) || value == null)
            return fallback;

        return value switch
        {
            bool b => b,
            string s when bool.TryParse(s, out var parsed) => parsed,
            int i => i != 0,
            long l => l != 0,
            double d => Math.Abs(d) > double.Epsilon,
            _ => fallback
        };
    }

    internal static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c))
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private sealed class Reader
    {
        private readonly string _json;
        private int _pos;

        internal Reader(string json) => _json = json ?? string.Empty;
        internal bool IsDone => _pos >= _json.Length;

        internal void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos]))
                _pos++;
        }

        internal object? ReadValue()
        {
            SkipWhitespace();
            if (_pos >= _json.Length)
                throw new FormatException("Unexpected end of JSON.");

            return _json[_pos] switch
            {
                '{' => ReadObject(),
                '[' => ReadArray(),
                '"' => ReadString(),
                't' => ReadLiteral("true", true),
                'f' => ReadLiteral("false", false),
                'n' => ReadLiteral("null", null),
                '-' or >= '0' and <= '9' => ReadNumber(),
                _ => throw new FormatException($"Unexpected JSON token at {_pos}.")
            };
        }

        private Dictionary<string, object?> ReadObject()
        {
            var obj = new Dictionary<string, object?>(StringComparer.Ordinal);
            _pos++;
            SkipWhitespace();
            if (TryConsume('}'))
                return obj;

            while (true)
            {
                SkipWhitespace();
                if (_pos >= _json.Length || _json[_pos] != '"')
                    throw new FormatException("Expected object key.");

                var key = ReadString();
                SkipWhitespace();
                Consume(':');
                obj[key] = ReadValue();
                SkipWhitespace();

                if (TryConsume('}'))
                    return obj;
                Consume(',');
            }
        }

        private List<object?> ReadArray()
        {
            var array = new List<object?>();
            _pos++;
            SkipWhitespace();
            if (TryConsume(']'))
                return array;

            while (true)
            {
                array.Add(ReadValue());
                SkipWhitespace();

                if (TryConsume(']'))
                    return array;
                Consume(',');
            }
        }

        private string ReadString()
        {
            Consume('"');
            var sb = new StringBuilder();
            while (_pos < _json.Length)
            {
                var c = _json[_pos++];
                if (c == '"')
                    return sb.ToString();

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (_pos >= _json.Length)
                    throw new FormatException("Unexpected end of escaped string.");

                var esc = _json[_pos++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 > _json.Length)
                            throw new FormatException("Invalid unicode escape.");
                        var hex = _json.Substring(_pos, 4);
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        _pos += 4;
                        break;
                    default:
                        throw new FormatException($"Invalid escape sequence: {esc}");
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private object ReadNumber()
        {
            var start = _pos;
            if (_json[_pos] == '-')
                _pos++;

            while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                _pos++;

            var isFloat = false;
            if (_pos < _json.Length && _json[_pos] == '.')
            {
                isFloat = true;
                _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                    _pos++;
            }

            if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
            {
                isFloat = true;
                _pos++;
                if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-'))
                    _pos++;
                while (_pos < _json.Length && char.IsDigit(_json[_pos]))
                    _pos++;
            }

            var raw = _json.Substring(start, _pos - start);
            if (isFloat)
                return double.Parse(raw, CultureInfo.InvariantCulture);

            return long.Parse(raw, CultureInfo.InvariantCulture);
        }

        private object? ReadLiteral(string literal, object? value)
        {
            if (_pos + literal.Length > _json.Length
                || string.CompareOrdinal(_json, _pos, literal, 0, literal.Length) != 0)
                throw new FormatException($"Expected {literal}.");

            _pos += literal.Length;
            return value;
        }

        private bool TryConsume(char expected)
        {
            if (_pos >= _json.Length || _json[_pos] != expected)
                return false;

            _pos++;
            return true;
        }

        private void Consume(char expected)
        {
            if (!TryConsume(expected))
                throw new FormatException($"Expected '{expected}'.");
        }
    }
}
