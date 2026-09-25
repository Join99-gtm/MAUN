using SamEngine;

namespace GooseDeluxe
{
    /// <summary>Is a screen point on the drawn goose (body, neck or head)? Used for the right-click menu.</summary>
    internal static class GooseHit
    {
        public static bool IsOnGoose(GoosePose p, Vector2 point)
        {
            if (p == null) return false;
            float s = p.scale;
            if (Vector2.Distance(point, p.bodyCenter) < 25f * s) return true;
            if (Vector2.Distance(point, p.neckHeadPoint) < 12f * s) return true;
            return Vector2.Distance(point, Vector2.Lerp(p.neckBase, p.neckHeadPoint, 0.5f)) < 10f * s;
        }
    }
}
