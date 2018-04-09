using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Thor;

namespace ThorTest
{
    [TestClass]
    public class Utilities_Test
    {
        [TestMethod]
        public void PickOne_PickingFromAnArrayOfNumbers_ShouldPickAnyOneOfThem()
        {
            List<int> arr = new List<int>() { 1, 2, 3, 4, 5 };
            HashSet<int> result = new HashSet<int>();
            
            while (result.Count < arr.Count)
            {
                result.Add(Utilities.Random.PickOne(arr.ToArray()));
            }

            Assert.AreEqual(result.Count, arr.Count);
        }

        [TestMethod]
        public void PickOneIf_PickingFromAnArrayOfNumbersWithPredicate_ShouldPickOnlyIfPredicatePasses()
        {
            List<int> arr = new List<int>() { 1, 2, 3, 4, 5 };
            HashSet<int> result = new HashSet<int>();

            while (result.Count < 3)
            {
                result.Add(Utilities.Random.PickOneIf(arr.ToArray(), (num) => { return num >= 3; }));
            }

            Assert.AreEqual(result.Count, 3);
            Assert.IsTrue(result.Contains(3));
            Assert.IsTrue(result.Contains(4));
            Assert.IsTrue(result.Contains(5));
        }
    }
}
