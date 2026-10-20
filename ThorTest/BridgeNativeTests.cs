using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;
using GTAVMods.Game;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ModUtilsTest
{
    [TestClass]
    public class BridgeNativeTests
    {
        private sealed class Marshaller : IArgumentMarshaller
        {
            public readonly List<object> Values = new List<object>();
            public readonly Queue<InputArgument> Results = new Queue<InputArgument>();
            public InputArgument Convert(object value) { Values.Add(value); return Results.Dequeue(); }
        }

        private sealed class RawNative : IRawNative
        {
            public Hash Hash;
            public InputArgument[] Arguments;
            public Type ResultType;
            public object Result;
            public Exception Failure;
            public void Call(Hash hash, InputArgument[] arguments)
            {
                Hash = hash; Arguments = arguments;
                if (Failure != null) throw Failure;
            }
            public T Call<T>(Hash hash, InputArgument[] arguments)
            {
                Call(hash, arguments); ResultType = typeof(T); return (T)Result;
            }
        }

        [TestMethod]
        public void HandleConversionPreservesPoolIdentityNullAndInputOrder()
        {
            var camera = new Camera(27);
            Assert.AreEqual(27, GameHandles.Of(camera));
            Assert.AreEqual(0, GameHandles.Of(null));
            CollectionAssert.AreEqual(new[] { 27, 0, 91 }, GameHandles.OfMany(new PoolObject[] { camera, null, new Camera(91) }));
        }

        [TestMethod]
        public void NativeInvokerMarshalsEachValueOnceInOrderAndPreservesTypedResult()
        {
            var raw = new RawNative { Result = 17 };
            var marshaller = new Marshaller();
            var first = new InputArgument(1UL);
            var second = new InputArgument(2UL);
            marshaller.Results.Enqueue(first); marshaller.Results.Enqueue(second);
            var invoker = new NativeInvoker(raw, marshaller);
            Assert.AreEqual(17, invoker.Call<int>(Hash.CREATE_OBJECT, "model", 1.25f));
            Assert.AreEqual(Hash.CREATE_OBJECT, raw.Hash);
            Assert.AreEqual(typeof(int), raw.ResultType);
            CollectionAssert.AreEqual(new object[] { "model", 1.25f }, marshaller.Values);
            Assert.AreSame(first, raw.Arguments[0]); Assert.AreSame(second, raw.Arguments[1]);
            Assert.HasCount(0, marshaller.Results);
            marshaller.Values.Clear();
            marshaller.Results.Enqueue(second);
            invoker.Call(Hash.DELETE_ENTITY, 42);
            Assert.AreEqual(Hash.DELETE_ENTITY, raw.Hash);
            CollectionAssert.AreEqual(new object[] { 42 }, marshaller.Values);
            Assert.AreSame(second, raw.Arguments.Single());
        }

        [TestMethod]
        public void ParameterlessNativeCallsNeedNoMarshallingAndPreserveFailures()
        {
            var raw = new RawNative();
            var invoker = new NativeInvoker(raw);
            invoker.Call(Hash.WAIT);
            Assert.AreEqual(Hash.WAIT, raw.Hash);
            Assert.HasCount(0, raw.Arguments);
            raw.Failure = new InvalidOperationException("native failure");
            Assert.AreSame(raw.Failure, Assert.ThrowsExactly<InvalidOperationException>(() => invoker.Call(Hash.WAIT)));
        }
    }
}
