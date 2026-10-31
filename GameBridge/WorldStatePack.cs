using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GTA;
using GTA.Math;
using GTA.Native;

namespace GTAVMods.Game
{
    public sealed class StateRay
    {
        public StateRay(float yaw, float pitch, float distance) { Yaw = yaw; Pitch = pitch; Distance = distance; }
        public readonly float Yaw, Pitch, Distance;
    }

    public sealed class StateQuery
    {
        public StateQuery(string[] sections, string[] types, float radius, int limit, int blipLimit, bool lineOfSight, StateRay[] rays, float probeHeight)
        { Sections = sections; Types = types; Radius = radius; Limit = limit; BlipLimit = blipLimit; LineOfSight = lineOfSight; Rays = rays; ProbeHeight = probeHeight; }
        public readonly string[] Sections, Types;
        public readonly float Radius, ProbeHeight;
        public readonly int Limit, BlipLimit;
        public readonly bool LineOfSight;
        public readonly StateRay[] Rays;
    }

    public sealed class StateCost
    {
        public int nearbyCandidates, returnedEntities, raycasts, lineOfSightTests, inventoryChecks, blipsRead;
    }

    public sealed class StateContext
    {
        public StateContext(StateQuery query, int player, Vector3 position, float heading)
        { Query = query; Player = player; Position = position; Heading = heading; }
        public readonly StateQuery Query;
        public readonly int Player;
        public readonly Vector3 Position;
        public readonly float Heading;
        public readonly StateCost Cost = new StateCost();
    }

    /// <summary>On-demand, extensible, structured observations. No polling or persistent entity cache.</summary>
    public sealed class WorldStatePack
    {
        private readonly GameServices game;
        private readonly INativeInvoker native;
        private readonly Func<Blip[]> blips;
        private readonly Dictionary<string, Func<StateContext, object>> sections = new Dictionary<string, Func<StateContext, object>>(StringComparer.Ordinal);
        public WorldStatePack(GameServices game, INativeInvoker native, Func<Blip[]> blips)
        {
            this.game = game; this.native = native; this.blips = blips;
            Register("player", Player); Register("vehicle", Vehicle); Register("weapons", Weapons);
            Register("inventory", Inventory); Register("nearby", Nearby); Register("navigation", Navigation);
            Register("probes", Probes); Register("camera", Camera);
        }
        public string[] Sections => sections.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        public void Register(string name, Func<StateContext, object> observe) => sections.Add(name, observe);

        public object Capture(StateQuery query)
        {
            var watch = Stopwatch.StartNew();
            int player = game.Host.PlayerPed;
            bool available = player != 0 && game.Entities.Exists(player);
            var context = new StateContext(query, player, available ? game.Entities.Position(player) : Vector3.Zero,
                available ? game.Entities.Rotation(player).Z : 0);
            var nodes = new Dictionary<string, object>();
            foreach (string name in query.Sections) nodes.Add(name, available ? sections[name](context) : null);
            return new {
                schema = "gta.world-state/1", gameTime = game.Host.GameTime, playerAvailable = available,
                units = new { distance = "metres", speed = "metres/second", angles = "degrees", relativeAxes = "right,forward,up", yaw = "positive right of player heading" },
                session = new { pauseMenu = game.Host.IsPaused, missionActive = native.Call<bool>(Hash.GET_MISSION_FLAG),
                    cutsceneActive = native.Call<bool>(Hash.IS_CUTSCENE_ACTIVE),
                    cutscenePlaying = native.Call<bool>(Hash.IS_CUTSCENE_PLAYING), cutsceneFinished = native.Call<bool>(Hash.HAS_CUTSCENE_FINISHED),
                    cutsceneTime = native.Call<int>(Hash.GET_CUTSCENE_TIME), cutsceneDuration = native.Call<int>(Hash.GET_CUTSCENE_TOTAL_DURATION),
                    screenFaded = native.Call<bool>(Hash.IS_SCREEN_FADED_OUT),
                    playerControl = native.Call<bool>(Hash.IS_PLAYER_CONTROL_ON, game.Host.PlayerHandle),
                    wantedLevel = native.Call<int>(Hash.GET_PLAYER_WANTED_LEVEL, game.Host.PlayerHandle),
                    phoneCall = native.Call<bool>(Hash.IS_MOBILE_PHONE_CALL_ONGOING),
                    helpDisplayed = native.Call<bool>(Hash.IS_HELP_MESSAGE_ON_SCREEN), messageDisplayed = native.Call<bool>(Hash.IS_MESSAGE_BEING_DISPLAYED) },
                sections = nodes, cost = context.Cost, captureMilliseconds = watch.Elapsed.TotalMilliseconds
            };
        }

        private object Player(StateContext c) => new {
            entity = Entity(c.Player, c, 1), maxHealth = game.Peds.MaxHealth(c.Player),
            armor = native.Call<int>(Hash.GET_PED_ARMOUR, c.Player),
            inVehicle = game.Peds.IsInVehicle(c.Player), vehicle = CurrentVehicle(c.Player),
            walking = game.Peds.IsWalking(c.Player), running = game.Peds.IsRunning(c.Player), sprinting = game.Peds.IsSprinting(c.Player),
            ragdoll = game.Peds.IsRagdoll(c.Player), inAir = game.Entities.IsInAir(c.Player),
            climbing = native.Call<bool>(Hash.IS_PED_CLIMBING, c.Player), vaulting = native.Call<bool>(Hash.IS_PED_VAULTING, c.Player),
            jumping = native.Call<bool>(Hash.IS_PED_JUMPING, c.Player), falling = native.Call<bool>(Hash.IS_PED_FALLING, c.Player),
            swimming = native.Call<bool>(Hash.IS_PED_SWIMMING, c.Player),
            inCover = native.Call<bool>(Hash.IS_PED_IN_COVER, c.Player, false),
            goingIntoCover = native.Call<bool>(Hash.IS_PED_GOING_INTO_COVER, c.Player),
            aimingFromCover = native.Call<bool>(Hash.IS_PED_AIMING_FROM_COVER, c.Player),
            coverFacingLeft = native.Call<bool>(Hash.IS_PED_IN_COVER_FACING_LEFT, c.Player),
            reloading = native.Call<bool>(Hash.IS_PED_RELOADING, c.Player),
            heightAboveGround = game.Entities.HeightAboveGround(c.Player)
        };

        private object Vehicle(StateContext c)
        {
            int vehicle = CurrentVehicle(c.Player);
            if (vehicle == 0 || !game.Entities.Exists(vehicle)) return null;
            return new { entity = Entity(vehicle, c, 2),
                speed = native.Call<float>(Hash.GET_ENTITY_SPEED, vehicle),
                engineHealth = native.Call<float>(Hash.GET_VEHICLE_ENGINE_HEALTH, vehicle),
                bodyHealth = native.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, vehicle),
                driveable = native.Call<bool>(Hash.IS_VEHICLE_DRIVEABLE, vehicle, false),
                onAllWheels = native.Call<bool>(Hash.IS_VEHICLE_ON_ALL_WHEELS, vehicle),
                upsideDown = native.Call<bool>(Hash.IS_ENTITY_UPSIDEDOWN, vehicle),
                engineRunning = native.Call<bool>(Hash.GET_IS_VEHICLE_ENGINE_RUNNING, vehicle),
                driver = native.Call<int>(Hash.GET_PED_IN_VEHICLE_SEAT, vehicle, -1, false),
                passengerCount = native.Call<int>(Hash.GET_VEHICLE_NUMBER_OF_PASSENGERS, vehicle, false, true),
                maxPassengers = native.Call<int>(Hash.GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS, vehicle)
            };
        }

        private object Weapons(StateContext c)
        {
            var weapon = game.Weapons.Current(c.Player);
            bool hasClip = game.Outputs.AmmoInClip(c.Player, (uint)weapon, out int clip);
            bool hasTarget = game.Outputs.AimTarget(game.Host.PlayerHandle, out int target);
            return new { selected = weapon.ToString(), hash = (uint)weapon,
                totalAmmo = native.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, c.Player, (uint)weapon),
                ammoInClip = hasClip ? (int?)clip : null,
                clipCapacity = native.Call<int>(Hash.GET_MAX_AMMO_IN_CLIP, c.Player, (uint)weapon, true),
                shooting = native.Call<bool>(Hash.IS_PED_SHOOTING, c.Player),
                readyToShoot = native.Call<bool>(Hash.IS_PED_WEAPON_READY_TO_SHOOT, c.Player),
                reloading = native.Call<bool>(Hash.IS_PED_RELOADING, c.Player),
                freeAiming = native.Call<bool>(Hash.IS_PLAYER_FREE_AIMING, game.Host.PlayerHandle),
                aimTarget = hasTarget && game.Entities.Exists(target) ? Entity(target, c) : null };
        }

        private object Inventory(StateContext c)
        {
            var owned = new List<object>();
            foreach (WeaponHash weapon in Enum.GetValues(typeof(WeaponHash)).Cast<WeaponHash>().Distinct())
            {
                c.Cost.inventoryChecks++;
                if (game.Weapons.HasWeapon(c.Player, weapon)) owned.Add(new { name = weapon.ToString(), hash = (uint)weapon,
                    totalAmmo = native.Call<int>(Hash.GET_AMMO_IN_PED_WEAPON, c.Player, (uint)weapon) });
            }
            return new { weapons = owned, scope = "Known SHVDNE WeaponHash values; custom hashes can be queried with command." };
        }

        private object Nearby(StateContext c)
        {
            var candidates = game.World.NearbyEntities(c.Position, c.Query.Radius);
            c.Cost.nearbyCandidates = candidates.Length;
            var all = candidates.Where(h => h != c.Player && game.Entities.Exists(h))
                .Select(h => new { handle = h, type = native.Call<int>(Hash.GET_ENTITY_TYPE, h) })
                .Where(e => c.Query.Types.Contains(TypeName(e.type)))
                .OrderBy(e => game.Entities.Position(e.handle).DistanceToSquared(c.Position)).ToArray();
            var nodes = all.Take(c.Query.Limit).Select(e => {
                c.Cost.returnedEntities++;
                object ped = null;
                if (e.type == 1) ped = new { human = native.Call<bool>(Hash.IS_PED_HUMAN, e.handle),
                    inCombatWithPlayer = game.Peds.IsInCombatAgainst(e.handle, c.Player),
                    weapon = game.Weapons.Current(e.handle).ToString(),
                    relationship = native.Call<int>(Hash.GET_RELATIONSHIP_BETWEEN_PEDS, e.handle, c.Player),
                    inVehicle = game.Peds.IsInVehicle(e.handle), vehicle = CurrentVehicle(e.handle) };
                bool? sight = null;
                if (c.Query.LineOfSight) { c.Cost.lineOfSightTests++; sight = native.Call<bool>(Hash.HAS_ENTITY_CLEAR_LOS_TO_ENTITY, c.Player, e.handle, 17); }
                return new { entity = Entity(e.handle, c, e.type), kind = TypeName(e.type), ped, lineOfSight = sight };
            }).ToArray();
            return new { radius = c.Query.Radius, total = all.Length, returned = nodes.Length, truncated = all.Length > nodes.Length,
                counts = new { peds = all.Count(e => e.type == 1), vehicles = all.Count(e => e.type == 2), objects = all.Count(e => e.type == 3) }, nodes };
        }

        private object Navigation(StateContext c)
        {
            var markers = blips().Select(blip => {
                c.Cost.blipsRead++;
                int handle = GameHandles.Of(blip);
                int sprite = native.Call<int>(Hash.GET_BLIP_SPRITE, handle);
                var position = native.Call<Vector3>(Hash.GET_BLIP_COORDS, handle);
                return new { handle, sprite, name = ((BlipSprite)sprite).ToString(), color = native.Call<int>(Hash.GET_BLIP_COLOUR, handle),
                    routed = native.Call<bool>(Hash.DOES_BLIP_HAVE_GPS_ROUTE, handle), waypoint = sprite == 8,
                    entity = native.Call<int>(Hash.GET_BLIP_INFO_ID_ENTITY_INDEX, handle), position = Vector(position), relative = Relative(position, c) };
            }).ToArray();
            // Contact/objective markers are commonly colored and can be created after dozens
            // of static shops. Keep them visible in a bounded query even without a GPS route.
            var sorted = markers.OrderByDescending(b => b.routed).ThenByDescending(b => b.waypoint)
                .ThenByDescending(b => b.color != 0 && b.sprite != 6 && b.sprite != 7).ThenBy(b => b.handle).ToArray();
            game.Outputs.StreetNames(c.Position, out uint street, out uint crossing);
            return new { zone = native.Call<string>(Hash.GET_NAME_OF_ZONE, c.Position.X, c.Position.Y, c.Position.Z),
                street = native.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, street), crossing = native.Call<string>(Hash.GET_STREET_NAME_FROM_HASH_KEY, crossing),
                routeFound = native.Call<bool>(Hash.GET_GPS_BLIP_ROUTE_FOUND), routeLength = native.Call<int>(Hash.GET_GPS_BLIP_ROUTE_LENGTH),
                gpsTargets = sorted.Where(b => b.routed || b.waypoint).Take(c.Query.BlipLimit).ToArray(),
                blips = sorted.Take(c.Query.BlipLimit).ToArray(), totalBlips = sorted.Length, truncated = sorted.Length > c.Query.BlipLimit };
        }

        private object Probes(StateContext c)
        {
            var origin = c.Position + new Vector3(0, 0, c.Query.ProbeHeight);
            int vehicle = CurrentVehicle(c.Player);
            int ignored = vehicle != 0 && game.Entities.Exists(vehicle) ? vehicle : c.Player;
            return c.Query.Rays.Select(ray => {
                var direction = StateGeometry.Direction(c.Heading, ray.Yaw, ray.Pitch);
                c.Cost.raycasts++;
                // Physical navigation must see collidable fences/glass even when LOS can see through them.
                var hit = game.World.Raycast(origin, origin + direction * ray.Distance, IntersectFlags.Everything,
                    ignored, ShapeTestOptions.IgnoreNoCollision);
                return new { yaw = ray.Yaw, pitch = ray.Pitch, range = ray.Distance, origin = Vector(origin), direction = Vector(direction),
                    hit = hit.DidHit, entity = hit.DidHit ? (int?)hit.Entity : null,
                    position = hit.DidHit ? Vector(hit.Position) : null,
                    distance = hit.DidHit ? (float?)origin.DistanceTo(hit.Position) : null };
            }).ToArray();
        }

        // Enhanced can retain the previous vehicle handle after the ped has left it.
        // Never interpret that handle alone as current occupancy.
        private int CurrentVehicle(int player) => game.Peds.IsInVehicle(player) ? native.Call<int>(Hash.GET_VEHICLE_PED_IS_IN, player, false) : 0;
        private object Camera(StateContext c) => new {
            position = Vector(game.Cameras.GameplayPosition), direction = Vector(game.Cameras.GameplayDirection), rotation = Vector(game.Cameras.GameplayRotation),
            rendered = new { position = Vector(native.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_COORD)),
                rotation = Vector(native.Call<Vector3>(Hash.GET_FINAL_RENDERED_CAM_ROT, 2)),
                fov = native.Call<float>(Hash.GET_FINAL_RENDERED_CAM_FOV) }
        };
        private object Entity(int handle, StateContext c, int type = 0)
        {
            int model = game.Entities.ModelHash(handle);
            return new { handle, model, modelName = ModelName(model, type),
            position = Vector(game.Entities.Position(handle)), relative = Relative(game.Entities.Position(handle), c),
            velocity = Vector(game.Entities.Velocity(handle)), rotation = Vector(game.Entities.Rotation(handle)),
            health = game.Entities.Health(handle), dead = game.Entities.IsDead(handle) };
        }
        // Resolve the already-read hash locally. Unknown/custom models and untyped
        // aim targets stay explicit nulls; a model name does not identify a mission role.
        private static string ModelName(int model, int type) => type == 1 ? Enum.GetName(typeof(PedHash), unchecked((uint)model))
            : type == 2 ? Enum.GetName(typeof(VehicleHash), unchecked((uint)model)) : null;
        private static string TypeName(int type) => type == 1 ? "ped" : type == 2 ? "vehicle" : "object";
        private static object Relative(Vector3 position, StateContext c)
        {
            var local = StateGeometry.Local(position - c.Position, c.Heading);
            return new { distance = position.DistanceTo(c.Position), right = local.X, forward = local.Y, up = local.Z,
                bearing = Math.Atan2(local.X, local.Y) * 180 / Math.PI };
        }
        private static object Vector(Vector3 value) => new { x = value.X, y = value.Y, z = value.Z };
    }

    public static class StateGeometry
    {
        public static Vector3 Direction(float heading, float yaw, float pitch)
        {
            double h = (heading - yaw) * Math.PI / 180, p = pitch * Math.PI / 180;
            return new Vector3((float)(-Math.Sin(h) * Math.Cos(p)), (float)(Math.Cos(h) * Math.Cos(p)), (float)Math.Sin(p));
        }
        public static Vector3 Local(Vector3 offset, float heading)
        {
            double h = heading * Math.PI / 180;
            return new Vector3((float)(offset.X * Math.Cos(h) + offset.Y * Math.Sin(h)),
                (float)(-offset.X * Math.Sin(h) + offset.Y * Math.Cos(h)), offset.Z);
        }
    }
}
