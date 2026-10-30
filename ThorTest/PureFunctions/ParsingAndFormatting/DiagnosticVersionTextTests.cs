using ADModUtils;
using Control = GTA.Control;

using GTA.Native;
using GTA;
using GTAVMods.Tests.Support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System;

namespace GTAVMods.Tests.PureFunctions.ParsingAndFormatting
{
    [TestClass, TestCategory("PureFunction")]
    public class DiagnosticVersionTextTests
    {
        private sealed class UnavailableDiagnostic { public override string ToString() => null; }

        [TestMethod]
        public void JsonPreservesEscapedTextNumericTypesAndNonFiniteObservations()
        {
            const string text = "path=\"C:\\game\"\r\n\tline\u0000";
            var data = new Dictionary<string, object> {
                { "text", text }, { "yes", true }, { "no", false }, { "missing", null },
                { "byte", (byte)2 }, { "sbyte", (sbyte)-2 }, { "short", (short)-3 }, { "ushort", ushort.MaxValue },
                { "int", 4 }, { "long", 5000000000L }, { "uint", uint.MaxValue }, { "ulong", ulong.MaxValue },
                { "decimal", 1.25m }, { "float", 0.5f }, { "double", -1.75d }, { "precise", 1.2345678901234567d },
                { "nan", float.NaN }, { "positiveInfinity", double.PositiveInfinity }, { "negativeInfinity", double.NegativeInfinity }
            };
            var original = CultureInfo.CurrentCulture;
            System.Xml.XmlElement record;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                record = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object> { { "event", "snapshot" } }, data));
            }
            finally { CultureInfo.CurrentCulture = original; }
            Assert.AreEqual(text, LogJson.Text(record, "data/text"));
            Assert.AreEqual("true", LogJson.Text(record, "data/yes"));
            Assert.AreEqual("false", LogJson.Text(record, "data/no"));
            Assert.AreEqual("null", record["data"]["missing"].GetAttribute("type"));
            foreach (var key in new[] { "byte", "sbyte", "short", "ushort", "int", "long", "uint", "ulong", "decimal", "float", "double" })
            {
                Assert.AreEqual("number", record["data"][key].GetAttribute("type"));
                Assert.AreEqual(Convert.ToString(data[key], CultureInfo.InvariantCulture), record["data"][key].InnerText);
            }
            Assert.AreEqual(1.2345678901234567d, double.Parse(record["data"]["precise"].InnerText, CultureInfo.InvariantCulture));
            Assert.AreEqual("NaN", record["data"]["nan"].InnerText);
            Assert.AreEqual("Infinity", record["data"]["positiveInfinity"].InnerText);
            Assert.AreEqual("-Infinity", record["data"]["negativeInfinity"].InnerText);
        }

        [TestMethod]
        public void MissingDataAndUnavailableTextRemainValidJsonAndLongTextIsBounded()
        {
            var longText = "diagnostic prefix:" + new string('x', 40000) + ":tail";
            var muchLongerText = "diagnostic prefix:" + new string('z', 160000) + ":tail";
            const string ordinaryText = "Hammer recalled; player=10, weapon=20.";
            var record = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object> { { "level", null } },
                new Dictionary<string, object> { { "text", longText }, { "ordinary", ordinaryText },
                    { "unavailable", new UnavailableDiagnostic() } }));
            var retainedText = record["data"]["text"].InnerText;
            Assert.IsTrue(retainedText.StartsWith("diagnostic prefix:", StringComparison.Ordinal));
            Assert.IsTrue(retainedText.Length < longText.Length, "Oversized diagnostic text must be bounded.");
            Assert.IsTrue(longText.StartsWith(retainedText, StringComparison.Ordinal),
                "Bounding text must retain an intact prefix rather than corrupt or reorder it.");
            var largerRecord = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object>(),
                new Dictionary<string, object> { { "text", muchLongerText } }));
            var retainedLargerText = largerRecord["data"]["text"].InnerText;
            Assert.AreEqual(retainedText.Length, retainedLargerText.Length,
                "Increasing oversized input must not increase the bounded record text.");
            Assert.IsTrue(muchLongerText.StartsWith(retainedLargerText, StringComparison.Ordinal));
            Assert.AreEqual(ordinaryText, record["data"]["ordinary"].InnerText,
                "Ordinary diagnostic messages must remain complete.");
            Assert.AreEqual("null", record["level"].GetAttribute("type"));
            Assert.AreEqual("null", record["data"]["unavailable"].GetAttribute("type"));
            var absent = LogJson.Parse(JsonLogFormat.Record(new Dictionary<string, object>(), null));
            Assert.AreEqual("object", absent["data"].GetAttribute("type"));
            Assert.AreEqual(0, absent["data"].ChildNodes.Count);
        }
    }
}
