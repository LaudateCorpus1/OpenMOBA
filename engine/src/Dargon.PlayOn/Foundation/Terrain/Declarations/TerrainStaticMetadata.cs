using System.Collections.Generic;
using System.Drawing;
using Dargon.PlayOn.Geometry;

namespace Dargon.PlayOn.Foundation.Terrain.Declarations {
   public class TerrainStaticMetadata {
      public string Name;
      public Rectangle LocalBoundary;
      public IReadOnlyList<Polygon2> LocalIncludedContours = new List<Polygon2>();
      public IReadOnlyList<Polygon2> LocalExcludedContours = new List<Polygon2>();
   }
}