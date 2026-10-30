using System;
using System.Collections.Generic;
using System.Globalization;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.PureFunctions.ValidationAndGameValues
{
    [TestClass, TestCategory("PureFunctions")]
    public class NativeScalarTests
    {
        public static IEnumerable<object[]> Encodings()
        {
            yield return new object[] { true, 1UL };
            yield return new object[] { false, 0UL };
            yield return new object[] { (byte)255, 255UL };
            yield return new object[] { (sbyte)-1, ulong.MaxValue };
            yield return new object[] { (short)-2, ulong.MaxValue - 1 };
            yield return new object[] { (ushort)65535, 65535UL };
            yield return new object[] { -3, ulong.MaxValue - 2 };
            yield return new object[] { uint.MaxValue, 4294967295UL };
            yield return new object[] { -4L, ulong.MaxValue - 3 };
            yield return new object[] { ulong.MaxValue, ulong.MaxValue };
            yield return new object[] { -0.75f, 0xBF400000UL };
            yield return new object[] { 0.5d, 0x3F000000UL };
            yield return new object[] { DayOfWeek.Tuesday, 2UL };
            yield return new object[] { new IntPtr(123), 123UL };
            yield return new object[] { new InputArgument(321UL), 321UL };
        }

        [TestMethod, DynamicData(nameof(Encodings))]
        public void NativeScalarsPreserveSignedValuesFloatBitsAndExistingArguments(object value, ulong expected)
        {
            Assert.IsTrue(NativeScalars.TryConvert(value, out var argument));
            Assert.AreEqual(expected.ToString(CultureInfo.InvariantCulture), argument.ToString());
        }

        [TestMethod]
        [DataRow(null)] [DataRow("pinned by SHVDNE")]
        public void ReferencesRequireTheHostMarshaller(object value)
        {
            Assert.IsFalse(NativeScalars.TryConvert(value, out var argument));
            Assert.IsNull(argument);
        }
    }
}
