using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using GTA;
using GTA.Math;

namespace GTAVMods.Agent
{
    // GTA vectors expose recursive convenience properties; entities expose live
    // native-backed properties. Console results must serialize only their values.
    public sealed class GameValueConverter : JavaScriptConverter
    {
        public override IEnumerable<Type> SupportedTypes => new[] { typeof(Vector3), typeof(Quaternion), typeof(PoolObject) };
        public override IDictionary<string, object> Serialize(object value, JavaScriptSerializer serializer)
        {
            if (value is Vector3 vector) return new Dictionary<string, object> { { "x", vector.X }, { "y", vector.Y }, { "z", vector.Z } };
            if (value is Quaternion rotation) return new Dictionary<string, object> { { "x", rotation.X }, { "y", rotation.Y }, { "z", rotation.Z }, { "w", rotation.W } };
            return new Dictionary<string, object> { { "handle", GTAVMods.Game.GameHandles.Of((PoolObject)value) } };
        }
        public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer)
            => throw new NotSupportedException("Game values are output-only; pass explicit numbers or entity handles as inputs.");
    }
}
