using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GTAVMods.Tests.Support
{
    internal sealed class FixedRandom : System.Random
    {
        private readonly int choice;
        private readonly double sample;
        public readonly List<int> Bounds = new List<int>();
        public FixedRandom(int choice = 1, double sample = 0.5) { this.choice = choice; this.sample = sample; }
        public override int Next(int maxValue) { Bounds.Add(maxValue); return choice; }
        public override int Next(int minValue, int maxValue) { Assert.AreEqual(0, minValue); return Next(maxValue); }
        public override double NextDouble() => sample;
    }
}
