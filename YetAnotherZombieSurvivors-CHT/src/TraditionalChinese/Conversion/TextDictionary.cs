using System.Text;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Conversion;

// Immutable after construction. Keys are UTF-16 paths; unmatched supplementary
// characters are copied intact, and dictionary keys may also be surrogate pairs.
public sealed class TextDictionary
{
    private sealed class Node
    {
        public readonly Dictionary<char, Node> Children = new();
        public string? Value;
    }

    private readonly Node root = new();
    public int Count { get; private set; }

    public static TextDictionary Load(params string[] paths) => Load(File.OpenRead, paths);

    public static TextDictionary Load(Func<string, Stream> open, params string[] names)
    {
        var dictionary = new TextDictionary();
        foreach (string name in names)
        {
            using Stream stream = open(name);
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            int lineNumber = 0;
            while (reader.ReadLine() is string raw)
            {
                lineNumber++;
                string line = raw.TrimStart('\uFEFF');
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1)
                    throw new FormatException($"{name}:{lineNumber}: expected source<TAB>target.");
                string source = line[..tab];
                // OpenCC alternatives are space-separated; the first is preferred.
                string target = line[(tab + 1)..].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    ?? throw new FormatException($"{name}:{lineNumber}: missing target.");
                dictionary.Add(source, target);
            }
        }
        return dictionary;
    }

    public static TextDictionary LoadOverrides(string path)
    {
        if (!File.Exists(path)) return new TextDictionary();
        using var reader = new StreamReader(path, new UTF8Encoding(false, true), true);
        return LoadOverrides(reader, Path.GetFileName(path));
    }

    // Caller owns the reader. Same validation for embedded and editable rules.
    public static TextDictionary LoadOverrides(TextReader reader, string name)
    {
        var dictionary = new TextDictionary();
        int lineNumber = 0;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is string raw)
        {
            lineNumber++;
            string line = raw.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
            int tab = line.IndexOf('\t');
            if (tab <= 0 || tab == line.Length - 1 || line.IndexOf('\t', tab + 1) >= 0)
                throw new FormatException($"{name}:{lineNumber}: expected exactly source<TAB>target.");
            if (!keys.Add(line[..tab]))
                throw new FormatException($"{name}:{lineNumber}: duplicate source key.");
            dictionary.Add(line[..tab], line[(tab + 1)..]);
        }
        return dictionary;
    }

    public void Add(string source, string target)
    {
        if (source.Length == 0) throw new ArgumentException("An empty dictionary key is not allowed.");
        Node node = root;
        foreach (char character in source)
        {
            if (!node.Children.TryGetValue(character, out Node? child))
                node.Children.Add(character, child = new Node());
            node = child;
        }
        // Earlier dictionaries win when a group contains duplicate keys.
        if (node.Value is null) { node.Value = target; Count++; }
    }

    public bool TryMatch(string text, int start, out int length, out string value)
    {
        Node node = root;
        length = 0;
        value = string.Empty;
        for (int index = start; index < text.Length; index++)
        {
            if (!node.Children.TryGetValue(text[index], out Node? next)) break;
            node = next;
            if (node.Value is not null)
            {
                length = index - start + 1;
                value = node.Value;
            }
        }
        return length != 0;
    }

    public string Replace(string text)
    {
        var result = new StringBuilder(text.Length);
        for (int index = 0; index < text.Length;)
        {
            if (TryMatch(text, index, out int length, out string value))
            {
                result.Append(value);
                index += length;
            }
            else result.Append(text[index++]);
        }
        return result.ToString();
    }
}
