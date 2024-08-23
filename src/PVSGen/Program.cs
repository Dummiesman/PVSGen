using System.Numerics;
using System.Diagnostics;
using System.Globalization;
using BepuPhysics.Collidables;
using BepuPhysics;
using BepuUtilities.Memory;
using PSDL;
using PVSGen.AGE;
using PVSGen.Compression;
using PVSGen.Physics;
using PVSGen.Extensions;

namespace PVSGen
{
    /// <summary>
    /// Generates CPVS files for Midtown Madness 2
    /// Some notes:
    /// - The PSDL library omits the special room 0 that is used in-engine, hence the +1/-1 offsets to room IDs
    /// - The Angel Game Engine uses an inverted X axis, which is why there's so much X axis flipping going on
    /// </summary>
    internal class PVSGen
    {
        // File system variables
        static string SdlPath = string.Empty;
        static string InstPath => Path.ChangeExtension(SdlPath, "inst");
        static string CityName => Path.GetFileNameWithoutExtension(SdlPath);
        static string RaceFolder => Path.Combine(new FileInfo(SdlPath).Directory.Parent.FullName, "race", CityName);
        static string GeometryFolder => Path.Combine(new FileInfo(SdlPath).Directory.Parent.FullName, "geometry");

        
        // Physics variables
        static CollidableProperty<int> RoomIdProps;
        static Simulation Simulation;
        static BufferPool BufferPool;

        // AGE Objects
        static PSDLFile SDL;
        static LevelInstanceList Instances;
        static PathSet BridgePathset;

        // Program variables
        static long TotalRaysFired = 0;
        static long TotalGroundRaysFired = 0;
        static long TotalRaysAlreadyOccluded = 0;
        static long TotalRaysHit = 0;
        
        static int RaysPerRoom = 1000000;
        static int DebugRoom = -1;
        
        static int MinRayHitsPerRoom = 0; // Minimum number of ray hits that must hit for a room to be considered visible
        static int MaxVisibleRooms = 9999; // Maximum rooms visible in any given room
        static float MaxDistance = 1500.0f; // Max raycast distance. Defaults to default in-game max view distance + 500
        static float HeightPadMin = 0.01f; // Small delta to avoid raycasting from exactly on the ground
        static float MaxCameraHeight = 15.0f; // MAXIMUM Extra camera height for non subterannean rooms
        static float MaxCameraHeightSubterranean = 5.0f; // MAXIMUM Extra camera height for subterannean rooms
        static HashSet<int>[] RoomVisibilities;

        static int PVSDoneRooms = 0; // for progress reporting

        // Helper functions
        static void ExportMeshSTL(string path, List<Triangle> triBuffer)
        {
            using (var writer = new BinaryWriter(File.Open(path, FileMode.Create)))
            {
                string stlheader = "Exported from PVSGen";
                for (int headerLen = 0; headerLen < 80; headerLen++)
                {
                    writer.Write((headerLen < stlheader.Length) ? stlheader[headerLen] : '\x00');
                }

                writer.Write(triBuffer.Count);
                for (int i = 0; i < triBuffer.Count; i++)
                {
                    var tri = triBuffer[i];
                    var a = tri.A.ToZUp(); var b = tri.B.ToZUp(); var c = tri.C.ToZUp();
                    var normal = Vector3.Normalize(Vector3.Cross(c - b, a - b));

                    writer.WriteVector3(normal);
                    writer.WriteVector3(a);
                    writer.WriteVector3(b);
                    writer.WriteVector3(c);

                    writer.Write((ushort)0); // no attributes
                }
            }
        }

        static int GetStatPercentage(long amount, long total)
        {
            return (int)(((double)amount / total) * 100.0d);
        }

        // PVS Generation
        static void GenerateRoomPVS(int roomIndex, HashSet<int> resultList)
        {
            var random = new Random(roomIndex); // seed random with room index
            List<RoomIdRay> debugRays = new List<RoomIdRay>();
            Dictionary<int, int> roomHitCounts = new Dictionary<int, int>();

            // compute room min/max bounds
            var room = SDL.Rooms[roomIndex];
            Vector3 roomMin = room.Perimeter[0].Vertex.ToVector3().Flipped();
            Vector3 roomMax = room.Perimeter[0].Vertex.ToVector3().Flipped();

            for(int i=1; i < room.Perimeter.Count; i++)
            {
                roomMin = Vector3.Min(roomMin, room.Perimeter[i].Vertex.ToVector3().Flipped());
                roomMax = Vector3.Max(roomMax, room.Perimeter[i].Vertex.ToVector3().Flipped());
            }

            // check if this room has any valid area to cast in
            // any room with 0 area is impossible to enter, and impossible to compute PVS data for
            if (Math.Abs(roomMax.X - roomMin.X) < float.Epsilon || Math.Abs(roomMax.Z - roomMin.Z) < float.Epsilon)
            {
                return;
            }

            // cast nsamples rays randomly
            for (int i=0; i < RaysPerRoom; i++)
            {
                float px = roomMin.X + (random.NextSingle() * (roomMax.X - roomMin.X));
                float pz = roomMin.Z + (random.NextSingle() * (roomMax.Z - roomMin.Z));

                // find ground position at px/pz, if applicable, then cast a random ray from 20m up
                var groundhandler = new SinglePointHitHandler(RoomIdProps, roomIndex, (room.Flags & RoomFlags.Instance) == 0);
                Simulation.RayCast(new Vector3(px, roomMax.Y + 512.0f, pz), new Vector3(0.0f, -1.0f, 0.0f), 1000.0f, ref groundhandler);
                Interlocked.Increment(ref TotalGroundRaysFired);
                
                if (groundhandler.Hit)
                {
                    float cameraHeight = HeightPadMin;
                    if((room.Flags & RoomFlags.Subterranean) == 0)
                    {
                        cameraHeight += random.NextSingle() * MaxCameraHeight;
                    }
                    else
                    {
                        cameraHeight += random.NextSingle() * MaxCameraHeightSubterranean;
                    }

                    float theta = random.NextSingle() * MathF.PI * 2.0f;
                    float phi = MathF.Acos(2.0f * random.NextSingle() - 1.0f);

                    float dx =  MathF.Sin(phi) * MathF.Cos(theta);
                    float dy =  MathF.Sin(phi) * MathF.Sin(theta);
                    float dz =  MathF.Cos(phi);

                    var castPosition = groundhandler.HitPos + (groundhandler.HitNormal * cameraHeight);
                    var castDirection = new Vector3(dx, dy, dz);

                    var hitHandler = new RoomIdHitHandler(RoomIdProps);
                    Simulation.RayCast(castPosition, castDirection, MaxDistance, ref hitHandler, i);
                    Interlocked.Increment(ref TotalRaysFired);

                    if (hitHandler.Hit)
                    {
                        Interlocked.Increment(ref TotalRaysHit);

                        int hitCount = roomHitCounts.GetValueOrDefault(hitHandler.HitRoom, 0);
                        roomHitCounts[hitHandler.HitRoom] = hitCount + 1;

                        if(roomIndex == DebugRoom) debugRays.Add(hitHandler.RayDebug);
                        if (hitCount != 0) Interlocked.Increment(ref TotalRaysAlreadyOccluded);
                    }
                }
            }

            if(roomIndex == DebugRoom)
            {
                var rayMeshBuilder = new RayMeshBuilder();
                foreach(var debugRay in debugRays)
                {
                    rayMeshBuilder.AddRay(debugRay.Origin, debugRay.HitPoint, 0.25f);
                }
                ExportMeshSTL($"{CityName}_room_{roomIndex+1}_rays.stl", rayMeshBuilder.Triangles);
            }

            // prune if we have too many hit rooms
            if(roomHitCounts.Count > MaxVisibleRooms)
            {
                int pruneCount = roomHitCounts.Count - MaxVisibleRooms;
                var roomsToRemove = roomHitCounts.OrderBy(x => x.Value).Take(pruneCount).Select(x => x.Key).ToList();
                foreach (var roomToRemove in roomsToRemove)
                {
                    roomHitCounts.Remove(roomToRemove);
                }
            }

            // prune rooms with too little ray hits
            {
                var roomsToRemove = roomHitCounts.Where(x => x.Value < MinRayHitsPerRoom).Select(x => x.Key).ToList();
                foreach (var roomToRemove in roomsToRemove)
                {
                    roomHitCounts.Remove(roomToRemove);
                }
            }

            // finally, add to the results list, including our own room and direct neighbors
            foreach(var hitRoomId in roomHitCounts.Keys)
            {
                resultList.Add(hitRoomId);
            }
            foreach(var connectedRoom in room.Perimeter.Where(x => x.ConnectedRoom != null).Select(x => x.ConnectedRoom))
            {
                resultList.Add(SDL.Rooms.IndexOf(connectedRoom));
            }
            Interlocked.Increment(ref PVSDoneRooms);
        }

        static void Usage()
        {
            Console.WriteLine($"PVSGen 1.1.0 - Created by Dummiesman, 2024");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("Usage: pvsgen <PSDL Path> -samples 1000000 -exportphysicsworld -maxdist 1500");
            Console.WriteLine("The program assumes the PSDL is in a game-ready directory structure, and will attempt to load INST, as well as PKG from the geometry folder one level above.");
            Console.WriteLine("This program uses as much CPU power as it can get, due to it's methodology (raytracing), your CPU usage may reach 100% for an extended period of time.");
            Console.WriteLine();
            Console.WriteLine("Supported Arguments (All arguments are optional):");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("-samples <amount> : Number of rays to cast per room. Defaults to 1 million. Higher numbers result in higher accuracy, at the cost of computation time and diminishing returns.");
            Console.WriteLine();
            Console.WriteLine("-maxdist <distance> : Maximum ray travel distance. Defaults to 1500.");
            Console.WriteLine();
            Console.WriteLine("-maxvisiblerooms <count> : Maximum number of rooms visible from any given room. Pruned via ray hit count. Defaults to 9999.");
            Console.WriteLine();
            Console.WriteLine("-minrayhits <count> : Minimum number of ray hits that must hit for a room to be considered visible. Defaults to 0 (e.g. if even just one ray hits, the room is considered visible).");
            Console.WriteLine();
            Console.WriteLine("-cameraheight <height> : Specify the maximum camera height that the generator will use (camera = ground pos + random(cameraheight)). Defaults to 15.");
            Console.WriteLine();
            Console.WriteLine("-undergroundcameraheight <height> : Specify the maximum camera height that the generator will use in underground rooms (camera = ground pos + random(undergroundcameraheight)). Defaults to 5.");
            Console.WriteLine();
            Console.WriteLine("-exportphysicsworld : Export the physics world to an STL file for debugging purposes.");
            Console.WriteLine();
            Console.WriteLine("-debugroom <number> : Specify a room number to export the raycast results for, in STL format, for debugging purposes. (WARNING: Filesize may be several hundred MB)");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine("Press ENTER to close program.");
            Console.ReadLine();
        }
       
        static void LoadCityFiles()
        {
            Console.WriteLine("Loading SDL...");
            try
            {
                SDL = new PSDLFile(SdlPath);
            }
            catch (Exception ex)
            {
                ConsoleUtil.WriteLineColored($"Failed to load SDL: {ex.Message}", ConsoleColor.Red);
                return;
            }


            Console.Write("Loading Instances... ");
            if (File.Exists(InstPath))
            {
                Instances = new LevelInstanceList();
                try
                {
                    using (var reader = new BinaryReader(File.OpenRead(InstPath)))
                    {
                        Instances.ReadBinary(reader);
                    }
                    Console.WriteLine("DONE");
                }
                catch (Exception ex)
                {
                    ConsoleUtil.WriteLineColored($"FAILED: {ex.Message}", ConsoleColor.Red);
                    Instances = null;
                }
            }
            else
            {
                Console.WriteLine("NOT FOUND");
            }

            Console.Write("Loading Bridges... ");
            string bridgesPath = Path.Combine(RaceFolder, $"{CityName}_bridge.pathset");
            if (File.Exists(bridgesPath))
            {
                try
                {
                    using (var reader = new BinaryReader(File.OpenRead(bridgesPath)))
                    {
                        BridgePathset = PathSet.ReadBinary(reader);
                    }
                    Console.WriteLine("DONE");
                }
                catch (Exception ex)
                {
                    ConsoleUtil.WriteLineColored($"FAILED: {ex.Message}", ConsoleColor.Red);
                    BridgePathset = null;
                }
            }
            else
            {
                Console.WriteLine("NOT FOUND");
            }
        }

        static void Main(string[] args)
        {
            Console.Title = $"Midtown Madness 2 PVS Generator";
            Console.ResetColor();

            if (args.Length == 0)
            {
                Usage();
                return;
            }

            if(!File.Exists(args[0]))
            {
                ConsoleUtil.WriteLineColored($"Input file doesn't exist.", ConsoleColor.Red);
                return;
            }

            Console.WriteLine("Initializing...");
            SdlPath = args[0];
            BufferPool = new BufferPool();
            Simulation = Simulation.Create(BufferPool, new NoCollisionCallbacks(), new EmptyPoseIntegratorCallbacks(), new SolveDescription(8, 1));
            RoomIdProps = new CollidableProperty<int>(Simulation, BufferPool);

            for(int i=0; i < args.Length - 1; i++)
            {
                if (args[i] == "-samples")
                {
                    _ = int.TryParse(args[i + 1], out RaysPerRoom);
                    i++;
                }
                else if (args[i] == "-minrayhits")
                {
                    _ = int.TryParse(args[i + 1], out MinRayHitsPerRoom);
                    i++;
                }
                else if (args[i] == "-debugroom")
                {
                    if(int.TryParse(args[i + 1], out DebugRoom))
                    {
                        DebugRoom--; // PSDL lib removes room 0
                    }
                    i++;
                }
                else if (args[i] == "-maxvisiblerooms")
                {
                    _ = int.TryParse(args[i + 1], out MaxVisibleRooms);
                    i++;
                }
                else if (args[i] == "-maxdist")
                {
                    _ = float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out MaxDistance);
                    i++;
                }
                else if (args[i] == "-cameraheight")
                {
                    _ = float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out MaxCameraHeight);
                    i++;
                }
                else if (args[i] == "-undergroundcameraheight")
                {
                    _ = float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out MaxCameraHeightSubterranean);
                    i++;
                }
            }

            // load city files
            ConsoleUtil.WriteLineColored("=== LOADING ===", ConsoleColor.Green);
            LoadCityFiles();

            // load city into physics system
            ConsoleUtil.WriteLineColored("=== PHYSICS INIT ===", ConsoleColor.Green);
            Console.WriteLine("Loading City Into Physics System...");
            var loader = new CityPhysicsBuilder(GeometryFolder, Instances, BufferPool, Simulation, SDL, BridgePathset, RoomIdProps);
            loader.Load();

            if (args.Contains("-exportphysicsworld"))
            {
                ExportMeshSTL($"{CityName}_physics.stl", loader.DebugMesh);
            }
        
            // compute pvs
            ConsoleUtil.WriteLineColored("=== PVS COMPUTATION ===", ConsoleColor.Green);
            Console.Write("Computing PVS... ");
            var stopwatch = Stopwatch.StartNew();

            RoomVisibilities = new HashSet<int>[SDL.Rooms.Count];

            var pvsTask = Task.Run(() =>
            {
                Parallel.For(0, SDL.Rooms.Count, (i) =>
                {
                    var visList = new HashSet<int>();
                    GenerateRoomPVS(i, visList);
                    RoomVisibilities[i] = visList;
                });
            });

            // wait for raycast completion
            using (var progress = new ProgressBar())
            {
                while (!pvsTask.IsCompleted)
                {
                    progress.Report((double)PVSDoneRooms / (double)SDL.Rooms.Count);
                    Thread.Sleep(20);
                }
                if(pvsTask.Exception != null)
                {
                    throw pvsTask.Exception;
                }
            }

            Console.WriteLine($"Done PVS calculation in {stopwatch.ElapsedMilliseconds}ms.");
            Console.Write($"{TotalGroundRaysFired} rays tested. ");
            Console.Write($"{TotalGroundRaysFired} ground tests. ");
            Console.Write($"{TotalRaysAlreadyOccluded} on known occluders, ");
            Console.Write($"{TotalRaysHit} ({GetStatPercentage(TotalRaysHit, TotalRaysFired)}%) hit, ");
            Console.Write($"{TotalRaysFired - TotalRaysHit} ({GetStatPercentage(TotalRaysFired - TotalRaysHit, TotalRaysFired)}%) missed.");
            Console.WriteLine();

            // warn if pvs is useless for this city
            bool useless = RoomVisibilities.All(x => x.Count == SDL.Rooms.Count);
            if(useless)
            {
                ConsoleUtil.WriteLineColored($"Every room is always visible in the generated PVS. The resulting file will be redundant.", ConsoleColor.Yellow);
            }

            // create raw pvs lists
            ConsoleUtil.WriteLineColored("=== DATA CREATION ===", ConsoleColor.Green);
            Console.WriteLine($"Creating PVS data...");
            List<byte[]> pvsLists = new List<byte[]>();
            int nrooms = SDL.Rooms.Count;

            for(int fromroom=0; fromroom < nrooms; fromroom++)
            {
                var visSet  = RoomVisibilities[fromroom];
                byte[] pvsData = new byte[(nrooms + 4) / 4]; // nrooms + empty room + 3 for uneven padding

                // use +1 in iteration count to account for room 0
                for(int toroom=1; toroom <= nrooms; toroom++)
                {
                    if (visSet.Contains(toroom - 1))
                    {
                        int byteIndex = toroom / 4;
                        int bitchshift = (toroom % 4) * 2;
                        pvsData[byteIndex] |= (byte)(3 << bitchshift);
                    }
                }

                pvsLists.Add(pvsData);
            }

            // compress, and track offsets
            Console.WriteLine($"Compressing PVS data...");
            MemoryStream compressedData = new MemoryStream();
            List<int> roomOffsets = new List<int>();
            
            for(int i=0; i < nrooms; i++)
            {
                roomOffsets.Add((int)compressedData.Length);
                var compressed = new RleCompressionCPVS().RleEncodeData(pvsLists[i]);
                compressedData.Write(compressed, 0, compressed.Length);
            }
            roomOffsets.Add((int)compressedData.Length);

            // write cpvs
            Console.WriteLine($"Saving PVS data...");

            compressedData.Seek(0, SeekOrigin.Begin);
            var outputCpvs = new CPVS(roomOffsets.ToArray(), compressedData.ToArray());
            string outPath = $"{CityName}.cpvs";
            
            using (var stream = File.Open(outPath, FileMode.Create))
            {
                outputCpvs.Write(stream);
            }

            // read back...
            Console.WriteLine($"Verifying written data...");
            var cpvs = new CPVS(outPath);
            for(int i=0; i < SDL.Rooms.Count; i++)
            {
                var fileData =  cpvs.Decompress(i);
                bool[] sourceData = Enumerable.Range(0, SDL.Rooms.Count + 1)
                                            .Select(room => RoomVisibilities[i].Contains(room - 1))
                                            .ToArray();
                
                // compare;
                for (int c=0; c < sourceData.Length; c++)
                {
                    if (c >= fileData.Length || sourceData[c] != fileData[c])
                    {
                        ConsoleUtil.WriteLineColored($"CPVS validation failure @ Room {i}:{c}", ConsoleColor.Red);
                    }
                }
            }

            Console.WriteLine("Complete! Press ENTER to close.");
            Console.ReadLine();
        }
    }
}