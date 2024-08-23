using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using PSDL;
using PSDL.Elements;
using PVSGen.BoundLoader;
using PVSGen.Extensions;
using System.Numerics;

namespace PVSGen.Physics
{
    internal class CityPhysicsBuilder
    {
        public readonly List<Triangle> DebugMesh = new List<Triangle>();

        private readonly string geometryDirectory;
        private readonly BufferPool bufferPool;
        private readonly Simulation simulation;
        private readonly PSDLFile sdl;
        private readonly CollidableProperty<int> roomIdProps;
        private readonly PathSet bridgePathset;
        private readonly LevelInstanceList instances;

        private static Vector3 BRIDGE_OFFSET = new Vector3(0, -0.3f, 0); // This looks like magic, but the game does this too.
        private static string BridgeFallbackModel = "giz_bridge01_l";

        static int[] StripToIndex(int start, int length, bool flip)
        {
            if (length < 3) return new int[0];

            int total = (length - 2) * 3;
            int[] index = new int[total];

            int count = 0;

            for (int i = 2; i < length; i++)
            {
                if (flip)
                {
                    index[count] = start + i - 1; count++;
                    index[count] = start + i - 2; count++;
                }
                else
                {
                    index[count] = start + i - 2; count++;
                    index[count] = start + i - 1; count++;
                }
                flip = !flip;
                index[count] = start + i;
                count++;
            }
            return index;
        }

        static bool ElementTexturesAreValid(ISDLElement element)
        {
            if (element.Textures == null || element.Textures[0] == null)
            {
                return false;
            }
            return true;
        }

        static int GatherCollidableElementCount(Room room)
        {
            int count = 0;
            foreach (var element in room.Elements)
            {
                if (element is not FacadeBoundElement && !ElementTexturesAreValid(element))
                {
                    continue;
                }
                if (element is not FacadeElement && element is not SliverElement)
                {
                    count++;
                }
            }
            return count;
        }

        private bool LoadPackageModel(string name, out PKGLoader model)
        {
            model = null;

            string modelPath = Path.Combine(geometryDirectory, $"{name}.pkg");
            if (File.Exists(modelPath))
            {
                Console.WriteLine($"Loading package model {name}...");

                var pkgLoader = new PKGLoader();
                using (var boundStream = File.OpenRead(modelPath))
                {
                    try
                    {
                        pkgLoader.Load(boundStream, "H", "M", "L", "VL");

                        if (pkgLoader.Triangles.Count > 0)
                        {
                            model = pkgLoader;
                            return true;
                        }
                        else
                        {
                            ConsoleUtil.WriteLineColored($"LoadPackageModel({name}) failed: Couldn't find any geometry.", ConsoleColor.Yellow);
                            return false;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        ConsoleUtil.WriteLineColored($"LoadPackageModel({name}) failed: {ex.Message}", ConsoleColor.Red);
                        return false;
                    }
                }
            }
            else
            {
                ConsoleUtil.WriteLineColored($"LoadPackageModel({name}) failed: Not found.", ConsoleColor.Yellow);
                return false;
            }
        }

        private void BuildRoadMesh(IRoad road, List<Triangle> outputList)
        {
            if (road.RowCount <= 1)
            {
                return;
            }

            if (road.RowBreadth == 2)
            {
                // walkways get their own generation case
                for (int i = 0; i < road.RowCount - 1; i++)
                {
                    var baseRow = road.GetRow(i);
                    var nextRow = road.GetRow(i + 1);

                    var tri = new Triangle
                    {
                        A = baseRow[0].ToVector3().Flipped(),
                        B = baseRow[1].ToVector3().Flipped(),
                        C = nextRow[0].ToVector3().Flipped()
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        A = baseRow[1].ToVector3().Flipped(),
                        B = nextRow[1].ToVector3().Flipped(),
                        C = nextRow[0].ToVector3().Flipped()
                    };
                    outputList.Add(tri);
                }
            }
            else
            {
                // other roads we only generate between sidewalk rows
                int triCount = (road.RowCount - 1) * 6;
                for (int i = 0; i < road.RowCount - 1; i++)
                {
                    var baseRow = road.GetSidewalkBoundary(i);
                    var nextRow = road.GetSidewalkBoundary(i + 1);

                    for (int j = 0; j < 4 - 1; j++)
                    {
                        var tri = new Triangle
                        {
                            A = baseRow[j].ToVector3().Flipped(),
                            B = baseRow[j + 1].ToVector3().Flipped(),
                            C = nextRow[j].ToVector3().Flipped()
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            A = baseRow[j + 1].ToVector3().Flipped(),
                            B = nextRow[j + 1].ToVector3().Flipped(),
                            C = nextRow[j].ToVector3().Flipped()
                        };
                        outputList.Add(tri);
                    }
                }
            }
        }

        private void BuildFacadeBoundMesh(FacadeBoundElement bound, List<Triangle> outputList)
        {
            var leftVertex = bound.Vertices[0];
            var rightVertex = bound.Vertices[1];

            var tri = new Triangle
            {
                A = leftVertex.ToVector3().Flipped(),
                B = rightVertex.ToVector3().Flipped(),
                C = leftVertex.ToVector3().Flipped().WithY(bound.Height)
            };
            outputList.Add(tri);

            tri = new Triangle
            {
                A = rightVertex.ToVector3().Flipped(),
                B = rightVertex.ToVector3().Flipped().WithY(bound.Height),
                C = leftVertex.ToVector3().Flipped().WithY(bound.Height)
            };
            outputList.Add(tri);
        }

        private void BuildCrosswalkMesh(CrosswalkElement crosswalk, List<Triangle> outputList)
        {
            var tri = new Triangle
            {
                C = crosswalk.Vertices[0].ToVector3().Flipped(),
                B = crosswalk.Vertices[1].ToVector3().Flipped(),
                A = crosswalk.Vertices[2].ToVector3().Flipped()
            };
            outputList.Add(tri);

            tri = new Triangle
            {
                C = crosswalk.Vertices[2].ToVector3().Flipped(),
                B = crosswalk.Vertices[1].ToVector3().Flipped(),
                A = crosswalk.Vertices[3].ToVector3().Flipped()
            };
            outputList.Add(tri);
        }

        private void BuildJunctionTunnelMesh(Room room, TunnelElement tunnel, List<Triangle> outputList)
        {
            List<Vector3> innerVertices = new List<Vector3>();
            List<Vector3> outerVertices = new List<Vector3>();
            Vector3 lastForwardDirection = Vector3.Zero;

            for (int i = 0; i < room.Perimeter.Count; i++)
            {
                Vector3 vertex = room.Perimeter[i].Vertex.ToVector3().Flipped();
                Vector3 vertexNext = room.Perimeter[(i + 1) % room.Perimeter.Count].Vertex.ToVector3().Flipped();
                Vector3 forwardDirection = Vector3.Normalize(vertexNext - vertex);

                if (forwardDirection.IsNaN())
                {
                    forwardDirection = lastForwardDirection;
                    if (forwardDirection.IsNaN())
                    {
                        throw new Exception($"HOSE! Multiple zero length edges in a row (P).");
                    }
                }

                //inner outer verts
                innerVertices.Add(vertex);
                outerVertices.Add(vertex + Vector3.Cross(forwardDirection, -Vector3.UnitY) * tunnel.WallWidth);
                lastForwardDirection = forwardDirection;
            }

            //draw
            // underside
            if ((tunnel.Flags & TunnelElement.TunnelFlags.IsWall) != 0)
            {
                for (int i = 2; i < room.Perimeter.Count; i++)
                {
                    var tri = new Triangle
                    {
                        A = outerVertices[0].WithY(outerVertices[0].Y - tunnel.WallUndersideDepth),
                        B = outerVertices[i].WithY(outerVertices[i].Y - tunnel.WallUndersideDepth),
                        C = outerVertices[i - 1].WithY(outerVertices[i - 1].Y - tunnel.WallUndersideDepth)
                    };
                    outputList.Add(tri);
                }
            }

            // ceiling
            if ((tunnel.Flags & (TunnelElement.TunnelFlags.FlatCeiling | TunnelElement.TunnelFlags.CurvedCeiling)) != 0)
            {
                for (int i = 2; i < room.Perimeter.Count; i++)
                {
                    var tri = new Triangle
                    {
                        C = innerVertices[0].WithY(innerVertices[0].Y + tunnel.Height),
                        B = innerVertices[i - 1].WithY(innerVertices[i - 1].Y + tunnel.Height),
                        A = innerVertices[i].WithY(innerVertices[i].Y + tunnel.Height)
                    };
                    outputList.Add(tri);
                }
            }

            // walls
            for (int i = 0; i < room.Perimeter.Count; i++)
            {
                if (tunnel.JunctionWalls[i + 1])
                {
                    // perimeter is CCW, so we grab vertex references the other way around
                    var vertB = innerVertices[i];
                    var vertA = innerVertices[(i + 1) % room.Perimeter.Count];
                    var vertBTop = vertB.WithY(vertB.Y + tunnel.Height);
                    var vertATop = vertA.WithY(vertA.Y + tunnel.Height);

                    var tri = new Triangle
                    {
                        A = vertA,
                        B = vertB,
                        C = vertATop
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        A = vertB,
                        B = vertBTop,
                        C = vertATop
                    };
                    outputList.Add(tri);

                    if ((tunnel.Flags & TunnelElement.TunnelFlags.IsWall) != 0)
                    {
                        var vertBOuter = outerVertices[i].WithY(outerVertices[i].Y - tunnel.WallUndersideDepth);
                        var vertAOuter = outerVertices[(i + 1) % room.Perimeter.Count].WithY(outerVertices[(i + 1) % room.Perimeter.Count].Y - tunnel.WallUndersideDepth);
                        var vertBOuterTop = vertBOuter.WithY(vertBOuter.Y + tunnel.Height + tunnel.WallUndersideDepth);
                        var vertAOuterTop = vertAOuter.WithY(vertAOuter.Y + tunnel.Height + tunnel.WallUndersideDepth);

                        // top part
                        tri = new Triangle
                        {
                            A = vertATop,
                            B = vertBTop,
                            C = vertAOuterTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            A = vertBTop,
                            B = vertBOuterTop,
                            C = vertAOuterTop
                        };
                        outputList.Add(tri);

                        // wall part
                        tri = new Triangle
                        {
                            C = vertAOuter,
                            B = vertBOuter,
                            A = vertBOuterTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            C = vertAOuter,
                            B = vertBOuterTop,
                            A = vertAOuterTop
                        };
                        outputList.Add(tri);
                    }
                }
            }
        }

        private void BuildRoadTunnelMesh(Room room, TunnelElement tunnel, List<Triangle> outputList)
        {
            // find road
            var roadForTunnel = room.Elements.FirstOrDefault(x => x is IRoad) as IRoad;
            if (roadForTunnel == null)
            {
                return;
            }

            //gather info
            int roadRowCount = roadForTunnel.RowCount;
            bool doubleSided = (tunnel.Flags & TunnelElement.TunnelFlags.Culled) != 0;

            var hasUndersideFlags = TunnelElement.TunnelFlags.LeftSide | TunnelElement.TunnelFlags.RightSide | TunnelElement.TunnelFlags.IsWall;
            bool hasUnderside = (tunnel.Flags & hasUndersideFlags) == hasUndersideFlags;

            var hasCeilingFlags = TunnelElement.TunnelFlags.FlatCeiling | TunnelElement.TunnelFlags.CurvedCeiling;
            bool hasCeiling = (tunnel.Flags & hasCeilingFlags) != 0;


            // generate vertices for walls
            List<Vector3> innerLeftVertices = new List<Vector3>();
            List<Vector3> outerLeftVertices = new List<Vector3>();
            List<Vector3> innerRightVertices = new List<Vector3>();
            List<Vector3> outerRightVertices = new List<Vector3>();

            Vector3 lastLeftForwardDirection = Vector3.Zero;
            Vector3 lastRightForwardDirection = Vector3.Zero;

            for (int i = 0; i < roadRowCount; i++)
            {
                var rowVerts = roadForTunnel.GetRow(i);
                Vector3 leftVertex = rowVerts[0].ToVector3().Flipped();
                Vector3 rightVertex = rowVerts[roadForTunnel.RowBreadth - 1].ToVector3().Flipped();

                Vector3 leftForwardDirection;
                Vector3 rightForwardDirection;

                if (i == roadRowCount - 1)
                {
                    var rowVertsPrev = roadForTunnel.GetRow(i - 1);
                    var leftVertexPrev = rowVertsPrev[0].ToVector3().Flipped();
                    var rightVertexPrev = rowVertsPrev[roadForTunnel.RowBreadth - 1].ToVector3().Flipped();

                    leftForwardDirection = Vector3.Normalize(leftVertex - leftVertexPrev);
                    rightForwardDirection = Vector3.Normalize(rightVertex - rightVertexPrev);
                }
                else
                {
                    var rowVertsNext = roadForTunnel.GetRow(i + 1);
                    var leftVertexNext = rowVertsNext[0].ToVector3().Flipped();
                    var rightVertexNext = rowVertsNext[roadForTunnel.RowBreadth - 1].ToVector3().Flipped();

                    leftForwardDirection = Vector3.Normalize(leftVertexNext - leftVertex);
                    rightForwardDirection = Vector3.Normalize(rightVertexNext - rightVertex);
                }

                if (leftForwardDirection.IsNaN() || rightForwardDirection.IsNaN())
                {
                    leftForwardDirection = lastLeftForwardDirection;
                    rightForwardDirection = lastRightForwardDirection;
                    if (leftForwardDirection.IsNaN() || rightForwardDirection.IsNaN())
                    {
                        throw new Exception($"HOSE! Multiple zero length edges in a row (R).");
                    }
                }

                innerLeftVertices.Add(leftVertex);
                innerRightVertices.Add(rightVertex);

                Vector3 calculatedOuterLeftVertex = leftVertex + Vector3.Cross(leftForwardDirection, Vector3.UnitY) * tunnel.WallWidth;
                Vector3 calculatedOuterRightVertex = rightVertex + Vector3.Cross(rightForwardDirection, -Vector3.UnitY) * tunnel.WallWidth;
                outerLeftVertices.Add(calculatedOuterLeftVertex);
                outerRightVertices.Add(calculatedOuterRightVertex);

                lastLeftForwardDirection = leftForwardDirection;
                lastRightForwardDirection = rightForwardDirection;
            }

            //draw
            // end caps
            if ((tunnel.Flags & TunnelElement.TunnelFlags.IsWall) != 0)
            {
                if ((tunnel.Flags & TunnelElement.TunnelFlags.ClosedStartLeft) != 0)
                {
                    var tri = new Triangle
                    {
                        C = innerLeftVertices[0],
                        B = outerLeftVertices[0],
                        A = outerLeftVertices[0].WithY(outerLeftVertices[0].Y + tunnel.Height)
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        C = innerLeftVertices[0],
                        B = outerLeftVertices[0].WithY(outerLeftVertices[0].Y + tunnel.Height),
                        A = innerLeftVertices[0].WithY(innerLeftVertices[0].Y + tunnel.Height)
                    };
                }
                if ((tunnel.Flags & TunnelElement.TunnelFlags.ClosedStartRight) != 0)
                {
                    var tri = new Triangle
                    {
                        A = innerRightVertices[0],
                        B = outerRightVertices[0],
                        C = outerRightVertices[0].WithY(outerRightVertices[0].Y + tunnel.Height)
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        A = innerRightVertices[0],
                        B = outerRightVertices[0].WithY(outerRightVertices[0].Y + tunnel.Height),
                        C = innerRightVertices[0].WithY(innerRightVertices[0].Y + tunnel.Height)
                    };
                    outputList.Add(tri);
                }
                if ((tunnel.Flags & TunnelElement.TunnelFlags.ClosedEndLeft) != 0)
                {
                    var tri = new Triangle
                    {
                        A = innerLeftVertices[roadRowCount - 1],
                        B = outerLeftVertices[roadRowCount - 1],
                        C = outerLeftVertices[roadRowCount - 1].WithY(outerLeftVertices[roadRowCount - 1].Y + tunnel.Height)
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        A = innerLeftVertices[roadRowCount - 1],
                        B = outerLeftVertices[roadRowCount - 1].WithY(outerLeftVertices[roadRowCount - 1].Y + tunnel.Height),
                        C = innerLeftVertices[roadRowCount - 1].WithY(innerLeftVertices[roadRowCount - 1].Y + tunnel.Height)
                    };
                    outputList.Add(tri);
                }
                if ((tunnel.Flags & TunnelElement.TunnelFlags.ClosedEndRight) != 0)
                {
                    var tri = new Triangle
                    {
                        C = innerRightVertices[roadRowCount - 1],
                        B = outerRightVertices[roadRowCount - 1],
                        A = outerRightVertices[roadRowCount - 1].WithY(outerRightVertices[roadRowCount - 1].Y + tunnel.Height)
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        C = innerRightVertices[roadRowCount - 1],
                        B = outerRightVertices[roadRowCount - 1].WithY(outerRightVertices[roadRowCount - 1].Y + tunnel.Height),
                        A = innerRightVertices[roadRowCount - 1].WithY(innerRightVertices[roadRowCount - 1].Y + tunnel.Height)
                    };
                    outputList.Add(tri);
                }
            }

            // underside
            if (hasUnderside)
            {
                for (int i = 0; i < roadRowCount - 1; i++)
                {
                    var vertLeftStart = outerLeftVertices[i].WithY(outerLeftVertices[i].Y - tunnel.WallUndersideDepth);
                    var vertRightStart = outerRightVertices[i].WithY(outerRightVertices[i].Y - tunnel.WallUndersideDepth);

                    var vertLeftNext = outerLeftVertices[i + 1].WithY(outerLeftVertices[i + 1].Y - tunnel.WallUndersideDepth);
                    var vertRightNext = outerRightVertices[i + 1].WithY(outerRightVertices[i + 1].Y - tunnel.WallUndersideDepth);

                    var tri = new Triangle
                    {
                        C = vertLeftStart,
                        B = vertRightStart,
                        A = vertLeftNext
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        C = vertRightStart,
                        B = vertRightNext,
                        A = vertLeftNext
                    };
                    outputList.Add(tri);
                }
            }

            // ceiling
            if (hasCeiling)
            {
                for (int i = 0; i < roadRowCount - 1; i++)
                {
                    var vertLeftStart = innerLeftVertices[i].WithY(innerLeftVertices[i].Y + tunnel.Height);
                    var vertRightStart = innerRightVertices[i].WithY(innerRightVertices[i].Y + tunnel.Height);

                    var vertLeftNext = innerLeftVertices[i + 1].WithY(innerLeftVertices[i + 1].Y + tunnel.Height);
                    var vertRightNext = innerRightVertices[i + 1].WithY(innerRightVertices[i + 1].Y + tunnel.Height);

                    var tri = new Triangle
                    {
                        C = vertLeftStart,
                        B = vertRightStart,
                        A = vertLeftNext
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        C = vertRightStart,
                        B = vertRightNext,
                        A = vertLeftNext
                    };
                    outputList.Add(tri);
                }
            }

            // walls
            for (int i = 0; i < roadRowCount - 1; i++)
            {
                if ((tunnel.Flags & TunnelElement.TunnelFlags.LeftSide) != 0)
                {
                    // left vertical slice
                    var vertLeftStart = innerLeftVertices[i];
                    var vertLeftStartTop = vertLeftStart.WithY(vertLeftStart.Y + tunnel.Height);
                    var vertLeftNext = innerLeftVertices[i + 1];
                    var vertLeftNextTop = vertLeftNext.WithY(vertLeftNext.Y + tunnel.Height);

                    var tri = new Triangle
                    {
                        A = vertLeftStart,
                        B = vertLeftNext,
                        C = vertLeftNextTop
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        A = vertLeftStart,
                        B = vertLeftNextTop,
                        C = vertLeftStartTop
                    };
                    outputList.Add(tri);

                    if (doubleSided)
                    {
                        tri = new Triangle
                        {
                            C = vertLeftStart,
                            B = vertLeftNext,
                            A = vertLeftNextTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            C = vertLeftStart,
                            B = vertLeftNextTop,
                            A = vertLeftStartTop
                        };
                        outputList.Add(tri);
                    }

                    if ((tunnel.Flags & TunnelElement.TunnelFlags.IsWall) != 0)
                    {
                        var vertLeftOuterStart = outerLeftVertices[i].WithY(outerLeftVertices[i].Y - tunnel.WallUndersideDepth);
                        var vertLeftOuterStartTop = vertLeftOuterStart.WithY(vertLeftOuterStart.Y + tunnel.Height + tunnel.WallUndersideDepth);
                        var vertLeftOuterNext = outerLeftVertices[i + 1].WithY(outerLeftVertices[i + 1].Y - tunnel.WallUndersideDepth);
                        var vertLeftOuterNextTop = vertLeftOuterNext.WithY(vertLeftOuterNext.Y + tunnel.Height + tunnel.WallUndersideDepth);

                        // top
                        tri = new Triangle
                        {
                            A = vertLeftOuterStartTop,
                            B = vertLeftStartTop,
                            C = vertLeftNextTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            A = vertLeftOuterStartTop,
                            B = vertLeftNextTop,
                            C = vertLeftOuterNextTop
                        };
                        outputList.Add(tri);

                        // side
                        tri = new Triangle
                        {
                            A = vertLeftOuterNext,
                            B = vertLeftOuterStart,
                            C = vertLeftOuterStartTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            A = vertLeftOuterNext,
                            B = vertLeftOuterStartTop,
                            C = vertLeftOuterNextTop
                        };
                        outputList.Add(tri);
                    }
                }

                if ((tunnel.Flags & TunnelElement.TunnelFlags.RightSide) != 0)
                {
                    // right vertical slice
                    var vertRightStart = innerRightVertices[i];
                    var vertRightStartTop = vertRightStart.WithY(vertRightStart.Y + tunnel.Height);
                    var vertRightNext = innerRightVertices[i + 1];
                    var vertRightNextTop = vertRightNext.WithY(vertRightNext.Y + tunnel.Height);

                    var tri = new Triangle
                    {
                        C = vertRightStart,
                        B = vertRightNext,
                        A = vertRightNextTop
                    };
                    outputList.Add(tri);

                    tri = new Triangle
                    {
                        C = vertRightStart,
                        B = vertRightNextTop,
                        A = vertRightStartTop
                    };
                    outputList.Add(tri);

                    if (doubleSided)
                    {
                        tri = new Triangle
                        {
                            A = vertRightStart,
                            B = vertRightNext,
                            C = vertRightNextTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            A = vertRightStart,
                            B = vertRightNextTop,
                            C = vertRightStartTop
                        };
                        outputList.Add(tri);

                    }

                    if ((tunnel.Flags & TunnelElement.TunnelFlags.IsWall) != 0)
                    {
                        var vertRightOuterStart = outerRightVertices[i].WithY(outerRightVertices[i].Y - tunnel.WallUndersideDepth);
                        var vertRightOuterStartTop = vertRightOuterStart.WithY(vertRightOuterStart.Y + tunnel.Height + tunnel.WallUndersideDepth);
                        var vertRightOuterNext = outerRightVertices[i + 1].WithY(outerRightVertices[i + 1].Y - tunnel.WallUndersideDepth);
                        var vertRightOuterNextTop = vertRightOuterNext.WithY(vertRightOuterNext.Y + tunnel.Height + tunnel.WallUndersideDepth);

                        // top
                        tri = new Triangle
                        {
                            C = vertRightOuterStartTop,
                            B = vertRightStartTop,
                            A = vertRightNextTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            C = vertRightOuterStartTop,
                            B = vertRightNextTop,
                            A = vertRightOuterNextTop
                        };
                        outputList.Add(tri);

                        // side
                        tri = new Triangle
                        {
                            C = vertRightOuterNext,
                            B = vertRightOuterStart,
                            A = vertRightOuterStartTop
                        };
                        outputList.Add(tri);

                        tri = new Triangle
                        {
                            C = vertRightOuterNext,
                            B = vertRightOuterStartTop,
                            A = vertRightOuterNextTop
                        };
                        outputList.Add(tri);
                    }
                }
            }
        }

        private void BuildTunnelMesh(Room room, TunnelElement tunnel, List<Triangle> outputList)
        {
            if (tunnel.IsJunctionTunnel)
            {
                BuildJunctionTunnelMesh(room, tunnel, outputList);
            }
            else
            {
                BuildRoadTunnelMesh(room, tunnel, outputList);
            }
        }

        private void BuildSidewalkStripMesh(SidewalkStripElement strip, List<Triangle> outputList)
        {
            if (strip.IsEndCap)
            {
                return;
            }

            var indices = StripToIndex(0, strip.Vertices.Count, false);
            if (indices.Length < 3)
            {
                return;
            }

            int triCount = indices.Length / 3;
            for (int i = 0; i < triCount; i++)
            {
                int indexBase = i * 3;

                var tri = new Triangle
                {
                    A = strip.Vertices[indices[indexBase + 0]].ToVector3().Flipped(),
                    B = strip.Vertices[indices[indexBase + 1]].ToVector3().Flipped(),
                    C = strip.Vertices[indices[indexBase + 2]].ToVector3().Flipped()
                };
                outputList.Add(tri);
            }
        }

        private void BuildRoofTriangleFanElement(RoofTriangleFanElement fan, List<Triangle> outputList)
        {
            if (fan.Vertices.Count < 3)
            {
                return;
            }

            for (int i = 2; i < fan.Vertices.Count; i++)
            {
                var tri = new Triangle
                {
                    A = fan.Vertices[0].ToVector3().Flipped().WithY(fan.Height),
                    B = fan.Vertices[i - 1].ToVector3().Flipped().WithY(fan.Height),
                    C = fan.Vertices[i].ToVector3().Flipped().WithY(fan.Height)
                };
                outputList.Add(tri);
            }
        }

        private void BuildTriangleFanElement(TriangleFanElement fan, List<Triangle> outputList)
        {
            if (fan.Vertices.Count < 3)
            {
                return;
            }

            for (int i = 2; i < fan.Vertices.Count; i++)
            {
                var tri = new Triangle
                {
                    A = fan.Vertices[0].ToVector3().Flipped(),
                    B = fan.Vertices[i - 1].ToVector3().Flipped(),
                    C = fan.Vertices[i].ToVector3().Flipped()
                };
                outputList.Add(tri);
            }
        }

        private void LoadRoomIntoSimulation(int rid)
        {
            var room = sdl.Rooms[rid];
            List<Triangle> roomTriangles = new List<Triangle>(1024);


            // Load instances
            foreach (var instance in instances.GetInstancesInRoom(rid + 1).Where(x => x.NeedsBound))
            {
                if(LoadPackageModel(instance.Name, out var instanceModel))
                {
                    instance.GetTransform(out var instancePos, out var instanceRot, out var instanceScale);

                    bufferPool.Take<Triangle>(instanceModel.Triangles.Count, out var triBuffer);
                    for (int i = 0; i < instanceModel.Triangles.Count; i++)
                    {
                        var tri = new Triangle
                        {
                            A = Vector3.Transform(instanceModel.Triangles[i].A * instanceScale, instanceRot) + instancePos,
                            B = Vector3.Transform(instanceModel.Triangles[i].B * instanceScale, instanceRot) + instancePos,
                            C = Vector3.Transform(instanceModel.Triangles[i].C * instanceScale, instanceRot) + instancePos
                        };
                        roomTriangles.Add(tri);
                    }
                }
            }
            

            // Load SDL
            foreach (var element in room.Elements)
            {
                if (element is not FacadeBoundElement && !ElementTexturesAreValid(element))
                {
                    continue;
                }

                if (element is RoofTriangleFanElement rtfe)
                {
                    BuildRoofTriangleFanElement(rtfe, roomTriangles);
                }
                else if (element is TriangleFanElement tfe)
                {
                    BuildTriangleFanElement(tfe, roomTriangles);
                }
                else if (element is FacadeBoundElement fb)
                {
                    BuildFacadeBoundMesh(fb, roomTriangles);
                }
                else if (element is IRoad re)
                {
                    BuildRoadMesh(re, roomTriangles);
                }
                else if (element is CrosswalkElement cwe)
                {
                    BuildCrosswalkMesh(cwe, roomTriangles);
                }
                else if (element is SidewalkStripElement sse)
                {
                    BuildSidewalkStripMesh(sse, roomTriangles);
                }
                else if (element is TunnelElement te)
                {
                    BuildTunnelMesh(room, te, roomTriangles);
                }
            }

            if (roomTriangles.Count > 0)
            {
                bufferPool.Take<Triangle>(roomTriangles.Count, out var triBuffer);
                for (int i = 0; i < roomTriangles.Count; i++)
                {
                    ref var tri = ref triBuffer[i];
                    tri.A = roomTriangles[i].A;
                    tri.B = roomTriangles[i].B;
                    tri.C = roomTriangles[i].C;
                };

                var roomShape = new Mesh(triBuffer, Vector3.One, bufferPool);
                var roomShapeIndex = simulation.Shapes.Add(roomShape);

                var roomShapeHandle = simulation.Statics.Add(new StaticDescription(RigidPose.Identity, roomShapeIndex));
                roomIdProps[roomShapeHandle] = rid;
                DebugMesh.AddRange(roomTriangles);
            }
        }

        private void AddBridgesToInstanceList()
        {
            // Generate bridge positions and orientations, 
            foreach (var path in bridgePathset.Paths)
            {
                string bridgeModelName;

                if (path.Name.Contains(':'))
                {
                    int splitIndex = path.Name.IndexOf(':');
                    bridgeModelName = path.Name.Substring(splitIndex + 1, path.Name.Length - splitIndex - 1);
                }
                else
                {
                    bridgeModelName = path.Name;
                }

                if (string.IsNullOrWhiteSpace(bridgeModelName) ||
                    !File.Exists(Path.Combine(geometryDirectory, $"{bridgeModelName}.pkg")))
                {
                    bridgeModelName = BridgeFallbackModel;
                }

                string bridgePkgPath = Path.Combine(geometryDirectory, $"{bridgeModelName}.pkg");
                if (!File.Exists(bridgePkgPath))
                {
                    ConsoleUtil.WriteLineColored($"Cannot find bridge model {bridgeModelName}.", ConsoleColor.Yellow);
                    continue;
                }

                // load bridge, and compute its size
                if(!LoadPackageModel(bridgeModelName, out var bridgeModel))
                {
                    continue;
                }

                // size computation
                Vector3 bridgeMin = bridgeModel.Triangles[0].A;
                Vector3 bridgeMax = bridgeModel.Triangles[0].A;
                for (int i = 0; i < bridgeModel.Triangles.Count; i++)
                {
                    bridgeMin = Vector3.Min(bridgeMin, bridgeModel.Triangles[i].A);
                    bridgeMin = Vector3.Min(bridgeMin, bridgeModel.Triangles[i].B);
                    bridgeMin = Vector3.Min(bridgeMin, bridgeModel.Triangles[i].C);
                    bridgeMax = Vector3.Max(bridgeMax, bridgeModel.Triangles[i].A);
                    bridgeMax = Vector3.Max(bridgeMax, bridgeModel.Triangles[i].B);
                    bridgeMax = Vector3.Max(bridgeMax, bridgeModel.Triangles[i].C);
                }

                var bridgeSize = bridgeMax - bridgeMin;

                // place bridges in the instance list
                Vector3 pathCenter = Vector3.Lerp(path.Points[0], path.Points[path.Points.Count - 1], 0.5f);
                pathCenter += BRIDGE_OFFSET;

                bool flipDirection = false; //defines what way the bridge will face
                for (int i = 0; i < path.Points.Count - 1; i++)
                {
                    Vector3 start = path.Points[i];
                    Vector3 end = path.Points[i + 1];

                    Vector3 bridgePos = flipDirection ? end.MoveTowards(start, bridgeSize.Z / 2f) :
                                                        start.MoveTowards(end, bridgeSize.Z / 2f);
                    bridgePos += BRIDGE_OFFSET;
                    var bridgeRot = Matrix4x4.CreateLookAt(bridgePos, pathCenter, Vector3.UnitY);

                    var instance = new LevelInstanceList.MatrixComponent()
                    {
                        Name = bridgeModelName,
                        Flags = InstanceFlags.Landmark,
                        RoomIndex = sdl.FindRoomId(-bridgePos.X, bridgePos.Y, bridgePos.Z) + 1 // MM2 coordinate space, +1 because this returns INDEX, excluding room 0
                    };

                    instance.SetTransform(bridgePos, Quaternion.CreateFromRotationMatrix(bridgeRot), Vector3.One);
                    instances.Instances.Add(instance);
                    flipDirection = !flipDirection;
                }
            }
            instances.BuildInstanceRoomMap();
        }

        private void FixupRoomFlags()
        {
            foreach(var instance in instances.Instances)
            {
                if(instance.RoomIndex > 0 && (instance.Flags & InstanceFlags.Landmark) != 0)
                {
                    var room = sdl.Rooms[instance.RoomIndex - 1];
                    room.Flags |= RoomFlags.Instance;
                }
            }
        }

        private void MergeFacadeBounds(Room room)
        {
            var elements = room.FindElementsOfType<FacadeBoundElement>();
            bool merged;
            do
            {
                merged = false;

                for (int i = elements.Count - 1; i >= 0; i--)
                {
                    for (int j = elements.Count - 1; j >= 0; j--)
                    {
                        if (j == i) continue;

                        var fba = elements[i];
                        var fbb = elements[j];

                        // check if these facade bounds touch each other at night
                        bool mergeARight = fba.Vertices[1].Equals(fbb.Vertices[0]);
                        bool mergeALeft = fba.Vertices[0].Equals(fbb.Vertices[1]);
                        
                        bool heightsMatch = fba.Height == fbb.Height;
                        bool anglesMatch = fba.LightAngle == fbb.LightAngle;

                        Vector3 edgeDirA = Vector3.Normalize((fba.Vertices[1] - fba.Vertices[0]).ToVector3());
                        Vector3 edgeDirB = Vector3.Normalize((fbb.Vertices[1] - fbb.Vertices[0]).ToVector3());
                        bool edgesMatch = Math.Abs(Vector3.Dot(edgeDirA, edgeDirB)) == 1.0f;

                        if ((mergeALeft || mergeARight) && heightsMatch && anglesMatch & edgesMatch)
                        {
                            if (mergeALeft)
                            {
                                fba.Vertices[0] = fbb.Vertices[0];
                            }
                            else
                            {
                                fba.Vertices[1] = fbb.Vertices[1];
                            }
                            merged = true;

                            elements.RemoveAt(j);
                            room.Elements.Remove(fbb);
                            break;
                        }
                    }
                }
            } while (merged);
        }

        private void OptimizeSDL()
        {
            // Merge facades for slightly faster runtime
            foreach (var room in sdl.Rooms)
            {
                MergeFacadeBounds(room);
            }
        }

        public void Load()
        {
            OptimizeSDL();
            AddBridgesToInstanceList();
            FixupRoomFlags();
            for (int i = 0; i < sdl.Rooms.Count; i++)
            {
                LoadRoomIntoSimulation(i);
            }
        }

        public CityPhysicsBuilder(string geometryDirectory, LevelInstanceList instances, BufferPool bufferPool, Simulation simulation, PSDLFile sdl, PathSet bridgePathSet, CollidableProperty<int> roomIdProps)
        {
            this.bridgePathset = bridgePathSet ?? new PathSet();
            this.geometryDirectory = geometryDirectory;
            this.instances = instances ?? new LevelInstanceList();
            this.bufferPool = bufferPool;
            this.simulation = simulation;
            this.sdl = sdl;
            this.roomIdProps = roomIdProps;
        }
    }
}
