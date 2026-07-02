namespace DspBlueprintTransform.Blueprint
{
    public static class BlueprintTransform
    {
        public static BlueprintData HorizontalOffset(BlueprintData bp, double offsetX, double offsetY)
        {
            var res = bp.Clone();
            foreach (var b in res.Buildings)
            {
                b.LocalOffset[0].X += offsetX;
                b.LocalOffset[1].X += offsetX;
                b.LocalOffset[0].Y += offsetY;
                b.LocalOffset[1].Y += offsetY;
            }
            return res;
        }
    }
}
