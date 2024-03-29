﻿using GTA.Math;

namespace Thor
{
    public class ThunderSegment
    {
        private Vector3 start;
        private Vector3 end;
        private static string THUNDER_FX_SET_NAME = "core";
        private static string THUNDER_FX_NAME = "muz_railgun";

        public ThunderSegment(Vector3 start, Vector3 end)
        {
            this.start = start;
            this.end = end;
        }

        public void Render()
        {
            var len = start.DistanceTo(end);
            var scale = len * 0.4f;
            var middle = (start + end) / 2;

            var startToEnd = ADModUtils.Utilities.Math.DirectionToRotation(end - start);
            var endToStart = ADModUtils.Utilities.Math.DirectionToRotation(start - end);

            ADModUtils.NativeHelper.PlayParticleFx(THUNDER_FX_SET_NAME, THUNDER_FX_NAME, start, ConvertToFxRotation(startToEnd), scale);
            ADModUtils.NativeHelper.PlayParticleFx(THUNDER_FX_SET_NAME, THUNDER_FX_NAME, end, ConvertToFxRotation(endToStart), scale);

            ADModUtils.NativeHelper.PlayParticleFx(THUNDER_FX_SET_NAME, THUNDER_FX_NAME, middle, ConvertToFxRotation(startToEnd), scale);
            ADModUtils.NativeHelper.PlayParticleFx(THUNDER_FX_SET_NAME, THUNDER_FX_NAME, middle, ConvertToFxRotation(endToStart), scale);
        }

        private Vector3 ConvertToFxRotation(Vector3 rot)
        {
            ADModUtils.Utilities.Swap(ref rot.X, ref rot.Y);
            rot.Z += 90.0f;
            rot.Y = -rot.Y;

            return rot;
        }

        public override string ToString()
        {
            return string.Format("{0} Start: {1}, End: {2}", base.ToString(), start, end);
        }
    }
}
