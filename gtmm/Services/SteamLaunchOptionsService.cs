using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace gtmm.Services;

public static class SteamLaunchOptionsService
{
    private const string AppId = SteamDetectorService.GorillaTagAppId;
    private const string BepInExLaunchOption = "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%";
    private const ulong SteamIdBase = 76561197960265728;
    private static readonly string[] AppConfigPath =
    {
        "UserLocalConfigStore", "Software", "Valve", "Steam", "apps", AppId
    };

    public static bool EnsureBepInExLaunchOption()
    {
        string configPath = FindActiveUserConfig();
        string original = File.ReadAllText(configPath);
        VdfDocument document = VdfDocument.Parse(original);

        VdfNode? appNode = document.FindPath(AppConfigPath);
        if (appNode?.Children == null)
        {
            if (appNode != null)
            {
                throw new InvalidDataException(
                    "Steam's Gorilla Tag settings are not in the expected format.");
            }
            string updated = document.AddPath(AppConfigPath, BepInExLaunchOption);
            WriteAtomically(configPath, updated);
            return true;
        }

        VdfNode? launchOptions = appNode.FindChild("LaunchOptions");
        string currentOptions = launchOptions?.Value ?? "";
        string desiredOptions = MergeLaunchOptions(currentOptions);
        if (currentOptions.Equals(desiredOptions, StringComparison.Ordinal))
        {
            return false;
        }

        string changed = launchOptions == null
            ? document.InsertValue(appNode, "LaunchOptions", desiredOptions)
            : document.ReplaceValue(launchOptions, desiredOptions);
        WriteAtomically(configPath, changed);
        return true;
    }

    private static string MergeLaunchOptions(string currentOptions)
    {
        string options = Regex.Replace(
            currentOptions,
            @"(?:^|\s)WINEDLLOVERRIDES\s*=\s*(?:""[^""]*""|'[^']*'|[^\s]+)",
            "",
            RegexOptions.IgnoreCase).Trim();
        options = $"{BepInExLaunchOption} {options}".Trim();
        if (!options.Contains("%command%", StringComparison.OrdinalIgnoreCase))
        {
            options += " %command%";
        }
        return options;
    }

    private static string FindActiveUserConfig()
    {
        foreach (string steamRoot in SteamDetectorService.GetStandardSteamPaths())
        {
            string configDirectory = Path.Combine(steamRoot, "config");
            string loginUsersPath = Path.Combine(configDirectory, "loginusers.vdf");
            if (!File.Exists(loginUsersPath))
            {
                continue;
            }

            string loginUsers = File.ReadAllText(loginUsersPath);
            Match activeUser = Regex.Match(
                loginUsers,
                @"""(?<steamId>\d{17})""\s*\{(?:(?!\r?\n\s*\}).)*?""MostRecent""\s*""1""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!activeUser.Success ||
                !ulong.TryParse(activeUser.Groups["steamId"].Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out ulong steamId) ||
                steamId < SteamIdBase)
            {
                continue;
            }

            string accountId = (steamId - SteamIdBase).ToString(CultureInfo.InvariantCulture);
            string userConfigPath = Path.Combine(
                steamRoot, "userdata", accountId, "config", "localconfig.vdf");
            if (File.Exists(userConfigPath))
            {
                return userConfigPath;
            }
        }

        throw new FileNotFoundException(
            "Could not find the active Steam account's settings. Open Steam and sign in, then try again.");
    }

    private static void WriteAtomically(string path, string contents)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Could not find the Steam settings folder.");
        string tempPath = Path.Combine(directory, $".localconfig-{Guid.NewGuid():N}.tmp");
        UnixFileMode? originalMode = null;
        if (OperatingSystem.IsLinux())
        {
            originalMode = File.GetUnixFileMode(path);
        }

        try
        {
            File.WriteAllText(tempPath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (OperatingSystem.IsLinux() && originalMode.HasValue)
            {
                File.SetUnixFileMode(tempPath, originalMode.Value);
            }
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class VdfNode(string key, string? value, int valueStart, int valueLength)
    {
        public string Key { get; } = key;
        public string? Value { get; } = value;
        public int ValueStart { get; } = valueStart;
        public int ValueLength { get; } = valueLength;
        public List<VdfNode>? Children { get; set; }
        public int ClosingBracePosition { get; set; }

        public VdfNode? FindChild(string childKey) =>
            Children?.FirstOrDefault(node =>
                node.Key.Equals(childKey, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class VdfDocument(string text, List<VdfNode> roots)
    {
        private readonly string _text = text;
        private readonly List<VdfNode> _roots = roots;

        public static VdfDocument Parse(string text)
        {
            var parser = new VdfParser(text);
            return new VdfDocument(text, parser.Parse());
        }

        public VdfNode? FindPath(string[] path)
        {
            VdfNode? node = _roots.FirstOrDefault(candidate =>
                candidate.Key.Equals(path[0], StringComparison.OrdinalIgnoreCase));
            for (int i = 1; node != null && i < path.Length; i++)
            {
                node = node.FindChild(path[i]);
            }
            return node;
        }

        public string ReplaceValue(VdfNode node, string value)
        {
            string encoded = Quote(value);
            return _text.Remove(node.ValueStart, node.ValueLength)
                .Insert(node.ValueStart, encoded);
        }

        public string InsertValue(VdfNode parent, string key, string value)
        {
            string indentation = GetIndentation(parent.ClosingBracePosition) + "\t";
            string insertion = $"{indentation}{Quote(key)} {Quote(value)}\n";
            return _text.Insert(parent.ClosingBracePosition, insertion);
        }

        public string AddPath(string[] path, string value)
        {
            VdfNode? parent = null;
            int existingPathLength = 0;
            if (_roots.FirstOrDefault(node =>
                    node.Key.Equals(path[0], StringComparison.OrdinalIgnoreCase)) is { } first)
            {
                parent = first;
                existingPathLength = 1;
                while (existingPathLength < path.Length &&
                       parent.FindChild(path[existingPathLength]) is { } next)
                {
                    parent = next;
                    existingPathLength++;
                }
            }

            string nested = BuildNestedBlock(path, existingPathLength, value, 0);
            if (parent == null)
            {
                return _text.TrimEnd() + "\n" + nested + "\n";
            }

            string indentation = GetIndentation(parent.ClosingBracePosition) + "\t";
            return _text.Insert(parent.ClosingBracePosition, $"{indentation}{nested}\n");
        }

        private string BuildNestedBlock(string[] path, int index, string value, int depth)
        {
            string indentation = new('\t', depth);
            string key = Quote(path[index]);
            if (index == path.Length - 1)
            {
                return $"{indentation}{key}\n{indentation}{{\n" +
                       $"{indentation}\t{Quote("LaunchOptions")} {Quote(value)}\n" +
                       $"{indentation}}}";
            }

            return $"{indentation}{key}\n{indentation}{{\n" +
                   $"{BuildNestedBlock(path, index + 1, value, depth + 1)}\n" +
                   $"{indentation}}}";
        }

        private string GetIndentation(int position)
        {
            int lineStart = _text.LastIndexOf('\n', Math.Max(0, position - 1));
            int index = lineStart + 1;
            while (index < position && _text[index] is ' ' or '\t')
            {
                index++;
            }
            return _text[(lineStart + 1)..index];
        }

        private static string Quote(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private sealed class VdfParser(string text)
    {
        private readonly string _text = text;
        private int _position;

        public List<VdfNode> Parse()
        {
            var nodes = ParseEntries(expectCloseBrace: false, out _);
            SkipWhitespaceAndComments();
            if (_position != _text.Length)
            {
                throw new InvalidDataException("Steam's settings file has an unexpected value.");
            }
            return nodes;
        }

        private List<VdfNode> ParseEntries(bool expectCloseBrace, out int closingBracePosition)
        {
            var nodes = new List<VdfNode>();
            while (true)
            {
                SkipWhitespaceAndComments();
                if (_position >= _text.Length)
                {
                    if (expectCloseBrace)
                    {
                        throw new InvalidDataException("Steam's settings file has an unclosed section.");
                    }
                    closingBracePosition = _position;
                    return nodes;
                }

                if (_text[_position] == '}')
                {
                    if (!expectCloseBrace)
                    {
                        throw new InvalidDataException("Steam's settings file has an unexpected closing brace.");
                    }
                    closingBracePosition = _position;
                    _position++;
                    return nodes;
                }

                string key = ReadToken(out _, out _);
                SkipWhitespaceAndComments();
                if (_position >= _text.Length)
                {
                    throw new InvalidDataException("Steam's settings file ends before a value.");
                }

                if (_text[_position] == '{')
                {
                    _position++;
                    var node = new VdfNode(key, null, 0, 0);
                    node.Children = ParseEntries(expectCloseBrace: true, out int closePosition);
                    node.ClosingBracePosition = closePosition;
                    nodes.Add(node);
                }
                else
                {
                    string value = ReadToken(out int valueStart, out int valueLength);
                    nodes.Add(new VdfNode(key, value, valueStart, valueLength));
                }
            }
        }

        private string ReadToken(out int tokenStart, out int tokenLength)
        {
            SkipWhitespaceAndComments();
            tokenStart = _position;
            if (_position >= _text.Length || _text[_position] is '{' or '}')
            {
                throw new InvalidDataException("Steam's settings file contains an invalid entry.");
            }

            if (_text[_position] == '"')
            {
                _position++;
                var value = new StringBuilder();
                bool closed = false;
                while (_position < _text.Length)
                {
                    char current = _text[_position++];
                    if (current == '"')
                    {
                        closed = true;
                        break;
                    }
                    if (current == '\\' && _position < _text.Length &&
                        _text[_position] is '"' or '\\')
                    {
                        current = _text[_position++];
                    }
                    value.Append(current);
                }

                if (!closed)
                {
                    throw new InvalidDataException("Steam's settings file contains an unfinished string.");
                }
                tokenLength = _position - tokenStart;
                return value.ToString();
            }

            while (_position < _text.Length &&
                   !char.IsWhiteSpace(_text[_position]) &&
                   _text[_position] is not '{' and not '}')
            {
                _position++;
            }

            tokenLength = _position - tokenStart;
            return _text.Substring(tokenStart, tokenLength);
        }

        private void SkipWhitespaceAndComments()
        {
            while (_position < _text.Length)
            {
                if (char.IsWhiteSpace(_text[_position]))
                {
                    _position++;
                    continue;
                }
                if (_position + 1 < _text.Length &&
                    _text[_position] == '/' &&
                    _text[_position + 1] == '/')
                {
                    _position += 2;
                    while (_position < _text.Length && _text[_position] is not '\r' and not '\n')
                    {
                        _position++;
                    }
                    continue;
                }
                break;
            }
        }
    }
}
