using System.Security;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Kimulator.Core.Assembly;

namespace Kimulator.App.Editor;

/// <summary>Syntax highlighting for the assembler, built from its own mnemonic and directive lists.</summary>
public static class AsmHighlighting
{
    private static IHighlightingDefinition? _definition;

    public static IHighlightingDefinition Definition => _definition ??= Build();

    private static IHighlightingDefinition Build()
    {
        string mnemonics = string.Join("", Assembler.MnemonicNames.Select(m => $"<Word>{SecurityElement.Escape(m)}</Word>"));
        string xshd = $"""
            <SyntaxDefinition name="6502" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="#6A9955" />
              <Color name="String" foreground="#CE9178" />
              <Color name="Mnemonic" foreground="#569CD6" fontWeight="bold" />
              <Color name="Directive" foreground="#C586C0" />
              <Color name="Number" foreground="#B5CEA8" />
              <Color name="Label" foreground="#DCDCAA" />
              <Color name="Register" foreground="#9CDCFE" />
              <RuleSet ignoreCase="true">
                <Span color="Comment" begin=";" />
                <Span color="String" begin="&quot;" end="&quot;" />
                <Keywords color="Mnemonic">{mnemonics}</Keywords>
                <Rule color="Label">^[@A-Za-z_][\w@.]*:?|^\s*[@A-Za-z_][\w@.]*:|^\s*:</Rule>
                <Rule color="Directive">\.[A-Za-z]+</Rule>
                <Rule color="Number">\$[0-9A-Fa-f]+|%[01]+|'.'?|\b[0-9]+\b</Rule>
                <Rule color="Register">,\s*[XxYy]\b</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;

        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
