using System.Text.RegularExpressions;
using System.Windows.Documents;

namespace NIRAAgent.UI.Presentation;

/// <summary>Bounded lexical coloring, not a compiler. Never edits or executes the source.</summary>
internal static class NIRACodeHighlighter
{
    private const string Common = "if else for foreach while do switch case default break continue return throw try catch finally new class struct interface enum public private protected internal static abstract virtual override sealed readonly const using namespace import from export extends implements async await yield in is as typeof var let function this super true false null void int long float double decimal bool boolean string char byte short object dynamic get set package final synchronized throws instanceof record select where with";
    private const string Python = "and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield match case print";
    private const string Sql = "select from where join inner outer left right full cross on as group by order asc desc having distinct union all insert into values update set delete create alter drop table view index primary key foreign references not null is and or between in exists case when then else end limit offset with begin commit rollback count sum avg min max true false";
    private const string Shell = "if else elseif fi then elif for foreach while do done function return switch case default try catch finally throw param begin process end class enum in export local echo exit unset source sudo";
    private const string Types = "Task ValueTask String Int32 Int64 Boolean Object Console List Dictionary IEnumerable IReadOnlyList CancellationToken Guid DateTime DateTimeOffset Exception Array Math System IDisposable IDisposableCollection Integer Double Float Boolean Character Number Promise Map Set RegExp Date StringBuilder NoneType str list dict tuple set bytes int float bool";
    private const string Quoted = "\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])*'";
    private const string CLikeStrings = "(?:\\$@|@\\$|@)\"(?:\"\"|[^\"])*\"|\\$?\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])*'|`(?:\\\\.|[^`\\\\])*`";
    private const string PyStrings = "(?:[rRbBuUfF]{0,2})(?:\"\"\"[\\s\\S]*?(?:\"\"\"|\\z)|'''[\\s\\S]*?(?:'''|\\z)|" + Quoted + ")";
    private const string Number = @"\b(?:0[xX][0-9A-Fa-f_]+|0[bB][01_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]*)\b";
    private const string Identifier = @"[\p{L}_$][\p{L}\p{N}_$]*";
    private sealed record Language(Regex Pattern, HashSet<string> Keywords, HashSet<string> TypeNames);
    private static readonly Language CLike = Make(@"//[^\r\n]*|/\*[\s\S]*?(?:\*/|\z)", CLikeStrings, Common);
    private static readonly Language Py = Make(@"\#[^\r\n]*", PyStrings, Python);
    private static readonly Language Json = Make(@"//[^\r\n]*|/\*[\s\S]*?(?:\*/|\z)", Quoted, "true false null");
    private static readonly Language SqlLang = Make(@"--[^\r\n]*|/\*[\s\S]*?(?:\*/|\z)", "'(?:''|[^'])*'|\"(?:\"\"|[^\"])*\"", Sql, ignoreCase: true);
    private static readonly Language ShellLang = Make(@"\#[^\r\n]*|<\#[\s\S]*?(?:\#>|\z)", Quoted, Shell, ignoreCase: true);
    private static readonly Language Markup = Make(@"<!--[\s\S]*?(?:-->|\z)", Quoted, "", markup: true);
    private static readonly Language Css = Make(@"/\*[\s\S]*?(?:\*/|\z)", Quoted, "important inherit initial unset auto none", ignoreCase: true);

    private static Language Make(string comment, string str, string keywords, bool ignoreCase = false, bool markup = false)
    {
        string pattern = "(?<comment>" + comment + ")|(?<string>" + str + ")|(?<number>" + Number + ")" +
            (markup ? @"|(?<type>(?<=<)[/!?]?[\w:.-]+)|(?<function>[\w:.-]+(?=\s*=))" : "") +
            "|(?<identifier>" + Identifier + @")|(?<operator>[+*/%!=<>|&^~?:;.,{}()\[\]\-])";
        var comparer = ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return new Language(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromMilliseconds(150)),
            new HashSet<string>(keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries), comparer),
            new HashSet<string>(Types.Split(' ', StringSplitOptions.RemoveEmptyEntries), comparer));
    }

    private static Language? Resolve(string label)
    {
        string name = label.Trim().Split(' ', '\t')[0].ToLowerInvariant();
        return name switch
        {
            "csharp" or "cs" or "c#" or "java" or "javascript" or "js" or "typescript" or "ts" or "jsx" or "tsx" or "c" or "cpp" or "c++" or "go" or "rust" or "rs" or "kotlin" or "kt" => CLike,
            "python" or "py" => Py,
            "json" or "jsonc" => Json,
            "sql" or "mysql" or "postgresql" or "tsql" => SqlLang,
            "powershell" or "ps1" or "pwsh" or "bash" or "sh" or "shell" or "zsh" or "yaml" or "yml" => ShellLang,
            "html" or "xml" or "xaml" or "svg" => Markup,
            "css" or "scss" => Css,
            _ => null
        };
    }

    internal static void Append(Paragraph paragraph, string source, string language)
    {
        NIRAResponsePalette.SetRole(paragraph, TextElement.ForegroundProperty, "code.foreground");
        NIRAResponsePalette.SetRole(paragraph, TextElement.BackgroundProperty, "code.background");
        Language? syntax = Resolve(language);
        if (syntax is null || source.Length > 120_000) { paragraph.Inlines.Add(new Run(source)); return; }
        int offset = 0, count = 0;
        try
        {
            foreach (Match match in syntax.Pattern.Matches(source))
            {
                if (++count > 6000) break;
                string? role = null;
                foreach (string token in new[] { "comment", "string", "number", "type", "function", "operator" })
                    if (match.Groups[token].Success) { role = token; break; }
                if (match.Groups["identifier"].Success)
                {
                    string word = match.Value;
                    if (syntax.Keywords.Contains(word)) role = "keyword";
                    else if (syntax.TypeNames.Contains(word)) role = "type";
                    else
                    {
                        int next = match.Index + match.Length;
                        while (next < source.Length && source[next] is ' ' or '\t') next++;
                        if (next < source.Length && source[next] == '(') role = "function";
                    }
                }
                if (role is null) continue;
                if (match.Index > offset) paragraph.Inlines.Add(new Run(source[offset..match.Index]));
                var run = new Run(match.Value);
                NIRAResponsePalette.SetRole(run, TextElement.ForegroundProperty, "code." + role);
                paragraph.Inlines.Add(run);
                offset = match.Index + match.Length;
            }
        }
        catch (RegexMatchTimeoutException) { /* Keep remaining source literal. */ }
        if (offset < source.Length) paragraph.Inlines.Add(new Run(source[offset..]));
    }
}
