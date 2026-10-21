using DeveloperConsole;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection;
using System;

namespace GTAVMods.Tests.PureFunctions.ParsingAndFormatting
{
    [TestClass, TestCategory("PureFunction")]
    public class CommandTextTests
    {
        [TestMethod]
        public void TokenizationPreservesQuotedWhitespaceEscapesAndNestedCodeWithoutExecutingIt() {
            var parsed = CommandParser.Tokenize(" \tcmd 42 -3 1.5 1e3 \"\" \"hello  world\" { if (true) { return \"}\"; } } \"a\\\"b\" {a\\}b} tail\n");
            CollectionAssert.AreEqual(new[] { "cmd", "42", "-3", "1.5", "1e3", "", "hello  world",
                " if (true) { return \"}\"; } ", "a\"b", "a}b", "tail" }, parsed.Select(t => t.Value).ToArray());
            CollectionAssert.AreEqual(new[] { CommandTokenKind.Word, CommandTokenKind.Number, CommandTokenKind.Number,
                CommandTokenKind.Number, CommandTokenKind.Number, CommandTokenKind.String, CommandTokenKind.String,
                CommandTokenKind.CodeBlock, CommandTokenKind.String, CommandTokenKind.CodeBlock, CommandTokenKind.Word }, parsed.Select(t => t.Key).ToArray());
        }

        [TestMethod]
        [DataRow("", "", 0)]
        [DataRow(" \r\n ", "", 0)]
        [DataRow("\"unfinished\\", "unfinished\\", 1)]
        [DataRow("{unfinished", "unfinished", 1)]
        [DataRow("\"", "", 1)]
        [DataRow("\"a\\qb\"", "a\\qb", 1)]
        [DataRow("{}", "", 1)]
        [DataRow("{\"tail\\", "\"tail\\", 1)]
        [DataRow("{@\"x\"", "@\"x\"", 1)]
        public void IncompleteInputRetainsTypedText(string input, string expected, int count) {
            var tokens = CommandParser.Tokenize(input);
            Assert.AreEqual(count, tokens.Count);
            Assert.AreEqual(expected, string.Join("", tokens.Select(t => t.Value)));
        }

        [TestMethod]
        [DataRow(" return '}'; ")]
        [DataRow(" return '{'; ")]
        [DataRow(" return '\\''; ")]
        [DataRow(" return '\\\\'; ")]
        [DataRow(" return @\"C:\\\"; ")]
        [DataRow(" return @\"a\"\"}b\"; ")]
        [DataRow(" return @\"a\"\"b\\\"; ")]
        [DataRow(" return @\"a\\}b\"; ")]
        [DataRow(" return \"a\\\"}b\"; ")]
        [DataRow("\"a\"")]
        public void CodeTokenKeepsLiteralDelimiters(string source) {
            var tokens = CommandParser.Tokenize("{" + source + "}");
            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(CommandTokenKind.CodeBlock, tokens[0].Key);
            Assert.AreEqual(source, tokens[0].Value);
        }
    }
}
