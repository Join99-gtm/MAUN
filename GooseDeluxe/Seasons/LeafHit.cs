using SamEngine;

namespace GooseDeluxe
{
    /// <summary>
    /// Where a leaf pile of the Autumn mod can be clicked. A pile is drawn around <c>pos</c>: its leaves lie
    /// in an ellipse of <c>rad</c> × 0.6·<c>rad</c> and are heaped up to about <c>rad</c> high (drawn at 0.6 of
    /// that), so the heap reaches roughly 1.2·<c>rad</c> above <c>pos</c> and 0.6·<c>rad</c> below it.
    /// The clickable area is an ellipse over all of that, a few pixels larger so a quick click still lands.
    /// </summary>
    internal static class LeafHit
    {
        public const float Margin = 6f;

        public static void Area(Vector2 pos, float rad, out float cx, out float cy, out float rx, out float ry)
        {
            cx = pos.x;
            cy = pos.y - 0.3f * rad;
            rx = rad + Margin;
            ry = 0.9f * rad + Margin;
        }

        public static bool IsOnPile(Vector2 pos, float rad, Vector2 point)
        {
            float cx, cy, rx, ry;
            Area(pos, rad, out cx, out cy, out rx, out ry);
            if (rx <= 0f || ry <= 0f) return false;
            float dx = (point.x - cx) / rx, dy = (point.y - cy) / ry;
            return dx * dx + dy * dy <= 1f;
        }
    }
}
