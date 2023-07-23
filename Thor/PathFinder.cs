//using GTA;
//using GTA.Math;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using static Thor.PathFinder.PathFinder;

//namespace Thor
//{
//    namespace PathFinder
//    {
//        internal enum PathFinderDestinationType
//        {
//            Player,
//            Target,
//        }

//        internal static class Constants
//        {
//            public static readonly PathFinderDirection[] PrimaryDirections = {
//                    PathFinderDirection.Front,
//                    PathFinderDirection.Up,
//                    PathFinderDirection.Left,
//                    PathFinderDirection.Right,
//                };

//            public static readonly PathFinderDirection[] SecondaryDirections = {
//                    PathFinderDirection.Back,
//                    PathFinderDirection.Down,
//                };

//            public const float RaycastStartingPointOffset = 1.0f;
//        }

//        internal static class Utils
//        {
//            public static readonly PathFinderDirection[] PrimaryDirections = {
//                    PathFinderDirection.Front,
//                    PathFinderDirection.Up,
//                    PathFinderDirection.Left,
//                    PathFinderDirection.Right,
//                };

//            public static readonly PathFinderDirection[] SecondaryDirections = {
//                    PathFinderDirection.Back,
//                    PathFinderDirection.Down,
//                };

//            public const float RaycastStartingPointOffset = 1.0f;
//        }

//        internal class PathFinder
//        {
//            internal enum PathFinderDirection
//            {
//                Up,
//                Down,
//                Left,
//                Right,
//                Front,
//                Back,
//            }

//            private Vector3 destination;
//            private Queue<Node> nodes;
//            private PathFinderState currentState;
//            private PathFinderRouteInstruction currentInstruction;
//            private PathFinderRouteInstructionWaitHandler waitHanlder;
//            private PathFinderRouteInstructionSpeedUpAndStopHandler speedUpAndStopHandler;
//            private PathFinderRouteInstructionSpeedUpAndSlowDownHandler speedUpAndSlowDownHandler;
//            private PathFinderRouteInstructionSpeedUpAndPassThroughHandler speedUpAndPassThroughHandler;
//            private Node nextViableNode;

//            private enum PathFinderState
//            {
//                Idle,
//                Deactivated,
//                CheckingPrimaryDirections,
//                CheckingSecondaryDirections,
//                ProcessingNextNode,
//                ProcessingNextInstruction,
//            }

//            private class BasePathFinderStateProcessor
//            {
//                private BasePathFinderStateProcessor instance;
//                protected PathFinderState state;

//                public BasePathFinderStateProcessor Instance
//                {
//                    get
//                    {
//                        if (instance == null)
//                        {
//                            instance = new BasePathFinderStateProcessor(state);
//                        }

//                        return instance;
//                    }
//                }
//                protected BasePathFinderStateProcessor(PathFinderState s)
//                {
//                    state = s;
//                }

//                public void Reset()
//                {
//                    instance = new BasePathFinderStateProcessor(state);
//                }

//                public virtual PathFinderState Process(PathFinderState currentState)
//                {
//                    if (currentState != state)
//                    {
//                        Reset();
//                        return currentState;
//                    }

//                    return ProcessCurrentState();
//                }

//                protected virtual PathFinderState ProcessCurrentState()
//                {
//                    return state;
//                }
//            }

//            private class BaseNodeFindingPathFinderStateProcessor : BasePathFinderStateProcessor
//            {
//                protected BaseNodeFindingPathFinderStateProcessor(PathFinderState s) : base(s) { }

//                public virtual PathFinderState Process(PathFinderState currentState, out Node nextNode)
//                {
//                    if (currentState != state)
//                    {
//                        Reset();
//                        nextNode = null;
//                        return currentState;
//                    }

//                    return ProcessCurrentState(out nextNode);
//                }

//                protected virtual PathFinderState ProcessCurrentState(out Node nextNode)
//                {
//                    nextNode = null;
//                    return state;
//                }
//            }

//            private class PathFinderState_Idle_Processor : BasePathFinderStateProcessor
//            {
//                private PathFinderState_Idle_Processor() : base(PathFinderState.Idle)
//                {

//                }
//            }

//            private class PathFinderState_Deactivated_Processor : BasePathFinderStateProcessor
//            {
//                private PathFinderState_Deactivated_Processor() : base(PathFinderState.Deactivated)
//                {

//                }
//            }

//            private class BaseDirectionCheckingPathFinderStateProcessor : BaseNodeFindingPathFinderStateProcessor
//            {
//                private uint currentDirectionIndex = 0;

//                protected BaseDirectionCheckingPathFinderStateProcessor(PathFinderState s) : base(s)
//                {
//                }

//                protected PathFinderDirection CurrentDirection
//                {
//                    get
//                    {
//                        if (currentDirectionIndex >= Constants.PrimaryDirections.Length)
//                        {
//                            throw new IndexOutOfRangeException("Invalid currentDirectionIndex");
//                        }

//                        return Constants.PrimaryDirections[currentDirectionIndex];
//                    }
//                }

//                protected override PathFinderState ProcessCurrentState(out Node nextNode)
//                {
//                    try
//                    {
//                        var currentDirection = CurrentDirection;
//                    }
//                    catch (IndexOutOfRangeException)
//                    {

//                        throw;
//                    }
//                }

//                protected void CheckCurrentDirection()
//                {

//                }
//            }

//            private class PathFinderState_CheckingPrimaryDirections_Processor : BaseDirectionCheckingPathFinderStateProcessor
//            {
//                private PathFinderState_CheckingPrimaryDirections_Processor() : base(PathFinderState.CheckingPrimaryDirections)
//                {
//                }

//                protected override sealed PathFinderState ProcessCurrentState(out Node nextNode)
//                {

//                }
//            }

//            private class PathFinderState_CheckingSecondaryDirections_Processor : BaseDirectionCheckingPathFinderStateProcessor
//            {
//                private PathFinderState_CheckingSecondaryDirections_Processor() : base(PathFinderState.CheckingSecondaryDirections)
//                {

//                }

//                protected override sealed PathFinderState ProcessCurrentState(out Node nextNode)
//                {

//                }
//            }

//            private class PathFinderState_ProcessingNextNode_Processor : BaseNodeFindingPathFinderStateProcessor
//            {
//                private PathFinderState_ProcessingNextNode_Processor() : base(PathFinderState.ProcessingNextNode)
//                {

//                }
//            }

//            private class PathFinderState_ProcessingNextInstruction_Processor : BasePathFinderStateProcessor
//            {
//                private PathFinderState_ProcessingNextInstruction_Processor() : base(PathFinderState.ProcessingNextInstruction)
//                {

//                }
//            }

//            private class Node
//            {
//                public Vector3 Position;

//                public Node(Vector3 pos)
//                {
//                    Position = pos;
//                }
//            }

//            public PathFinder()
//            {
//                currentState = PathFinderState.Deactivated;
//                nodes = new Queue<Node>();
//            }

//            public delegate void PathFinderRouteInstructionWaitHandler();

//            public delegate void PathFinderRouteInstructionSpeedUpAndStopHandler();

//            public delegate void PathFinderRouteInstructionSpeedUpAndSlowDownHandler();

//            public delegate void PathFinderRouteInstructionSpeedUpAndPassThroughHandler();

//            public void RegisterWaitHandler(PathFinderRouteInstructionWaitHandler handler)
//            {
//                waitHanlder = handler;
//            }
//            public void RegisterSpeedUpAndStopHandler(PathFinderRouteInstructionSpeedUpAndStopHandler handler)
//            {
//                speedUpAndStopHandler = handler;
//            }
//            public void RegisterSpeedUpAndSlowDownHandler(PathFinderRouteInstructionSpeedUpAndSlowDownHandler handler)
//            {
//                speedUpAndSlowDownHandler = handler;
//            }
//            public void RegisterSpeedUpAndPassThroughHandler(PathFinderRouteInstructionSpeedUpAndPassThroughHandler handler)
//            {
//                speedUpAndPassThroughHandler = handler;
//            }

//            public void OnTick(Vector3 nextDestination, PathFinderDestinationType type)
//            {
//                destination = nextDestination;

//                // Find next node
//                //  check primary directions facing the destination
//                SearchForNextNode();
//                //  if any feasible node is found
//                //      goto 1
//                //  otherwise check secondary directions facing the destination
//                //  if any feasible node is found
//                //      goto 1
//                //  otherwise deactivate the path finder
//                //
//                // 1:
//                // check the surroundings of where the node is and determine the instruction
//            }

//            public void Activate()
//            {
//                currentState = PathFinderState.Idle;
//            }

//            public void Deactivate()
//            {
//                currentState = PathFinderState.Deactivated;
//            }

//            private void SearchForNextNode()
//            {
//                CheckPrimaryDirections();

//                if (nextViableNode == null)
//                {
//                    CheckSeondaryDirections();
//                }
//            }

//            private void CheckPrimaryDirections()
//            {

//            }
//        }
//        enum PathFinderRouteInstructionType
//        {
//            Wait,
//            SpeedUpAndStop,
//            SpeedUpAndSlowDown,
//            SpeedUpAndPassThrough,
//        }

//        internal class PathFinderRouteInstruction
//        {
//            private readonly PathFinderRouteInstructionType _type;
//            public PathFinderRouteInstruction(PathFinderRouteInstructionType type)
//            {
//                _type = type;
//            }

//            public PathFinderRouteInstructionType Type
//            {
//                get { return _type; }
//            }
//        }
//    }
//}
