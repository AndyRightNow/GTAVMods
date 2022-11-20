using GTA.Math;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Thor
{
    namespace PathFinder
    {

        internal class PathFinder
        {
            private Vector3 destination;
            private bool isActivated;
            private PathFinderRouteInstructionWaitHandler waitHanlder;
            private PathFinderRouteInstructionSpeedUpAndStopHandler speedUpAndStopHandler;
            private PathFinderRouteInstructionSpeedUpAndSlowDownHandler speedUpAndSlowDownHandler;
            private PathFinderRouteInstructionSpeedUpAndPassThroughHandler speedUpAndPassThroughHandler;

            public PathFinder()
            {
                isActivated = false;
            }

            public delegate void PathFinderRouteInstructionWaitHandler();

            public delegate void PathFinderRouteInstructionSpeedUpAndStopHandler();

            public delegate void PathFinderRouteInstructionSpeedUpAndSlowDownHandler();

            public delegate void PathFinderRouteInstructionSpeedUpAndPassThroughHandler();

            public void RegisterWaitHandler(PathFinderRouteInstructionWaitHandler handler)
            {
                waitHanlder = handler;
            }
            public void RegisterSpeedUpAndStopHandler(PathFinderRouteInstructionSpeedUpAndStopHandler handler)
            {
                speedUpAndStopHandler = handler;
            }
            public void RegisterSpeedUpAndSlowDownHandler(PathFinderRouteInstructionSpeedUpAndSlowDownHandler handler)
            {
                speedUpAndSlowDownHandler = handler;
            }
            public void RegisterSpeedUpAndPassThroughHandler(PathFinderRouteInstructionSpeedUpAndPassThroughHandler handler)
            {
                speedUpAndPassThroughHandler = handler;
            }

            public void OnTick(Vector3 nextDestination)
            {
                destination = nextDestination;
            }

            public void Activate()
            {

            }

            public void Deactivate()
            {

            }
        }
        enum PathFinderRouteInstructionType
        {
            Wait,
            SpeedUpAndStop,
            SpeedUpAndSlowDown,
            SpeedUpAndPassThrough,
        }

        internal class PathFinderRouteInstruction
        {
            private readonly PathFinderRouteInstructionType _type;
            public PathFinderRouteInstruction(PathFinderRouteInstructionType type)
            {
                _type = type;
            }

            public PathFinderRouteInstructionType Type
            {
                get { return _type; }
            }
        }
    }
}
