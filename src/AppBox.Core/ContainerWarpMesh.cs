namespace AppBox.Core;

public enum ContainerAnimationStyle
{
    Genie,
    Suck,
    Scale,
    MagicLamp,
    Origami,
    PageCurl,
    BlackHole,
    Flip3D,
    Jelly,
    Random
}

public readonly record struct WarpVertex(double X, double Y, double Z, double U, double V);

/// <summary>
/// Pure 3-D mesh deformation mathematics for macOS-style Genie, Suck, Scale,
/// Magic Lamp, Origami Fold, Page Curl, Black Hole Vortex, Flip 3-D, and Jelly animations.
/// </summary>
public static class ContainerWarpMesh
{
    public static readonly ContainerAnimationStyle[] ConcreteStyles =
    [
        ContainerAnimationStyle.Genie,
        ContainerAnimationStyle.Suck,
        ContainerAnimationStyle.Scale,
        ContainerAnimationStyle.MagicLamp,
        ContainerAnimationStyle.Origami,
        ContainerAnimationStyle.PageCurl,
        ContainerAnimationStyle.BlackHole,
        ContainerAnimationStyle.Flip3D,
        ContainerAnimationStyle.Jelly
    ];

    public static ContainerAnimationStyle GetRandomConcreteStyle(ContainerAnimationStyle? except = null)
    {
        if (except is { } exp && ConcreteStyles.Length > 1)
        {
            var candidates = ConcreteStyles.Where(s => s != exp).ToArray();
            return candidates[Random.Shared.Next(candidates.Length)];
        }
        return ConcreteStyles[Random.Shared.Next(ConcreteStyles.Length)];
    }

    public static ContainerAnimationStyle ResolveStyle(ContainerAnimationStyle style, ContainerAnimationStyle? except = null) =>
        style == ContainerAnimationStyle.Random ? GetRandomConcreteStyle(except) : style;

    public static int GetDurationMs(ContainerAnimationStyle style, bool isOpening, int targetDurationMs = 0)
    {
        if (targetDurationMs <= 0) targetDurationMs = 320;
        double ratio = Math.Clamp(targetDurationMs, 50, 3000) / 320.0;
        int baseMs = style switch
        {
            ContainerAnimationStyle.Genie => isOpening ? 340 : 250,
            ContainerAnimationStyle.Suck => isOpening ? 310 : 235,
            ContainerAnimationStyle.Scale => isOpening ? 210 : 155,
            ContainerAnimationStyle.MagicLamp => isOpening ? 360 : 265,
            ContainerAnimationStyle.Origami => isOpening ? 350 : 255,
            ContainerAnimationStyle.PageCurl => isOpening ? 345 : 255,
            ContainerAnimationStyle.BlackHole => isOpening ? 360 : 265,
            ContainerAnimationStyle.Flip3D => isOpening ? 290 : 215,
            ContainerAnimationStyle.Jelly => isOpening ? 370 : 240,
            _ => isOpening ? 300 : 220
        };
        return Math.Max(40, (int)Math.Round(baseMs * ratio));
    }

    public static string GetTitle(ContainerAnimationStyle style) => LocalizationService.Get($"Animation.{style}.Title");

    public static string GetTagline(ContainerAnimationStyle style) => LocalizationService.Get($"Animation.{style}.Tagline");

    public static string GetDescription(ContainerAnimationStyle style) => LocalizationService.Get($"Animation.{style}.Description");

    public static double ComputeCollapseProgress(ContainerAnimationStyle style, double normalizedTime, bool isOpening)
    {
        double t = Math.Clamp(normalizedTime, 0.0, 1.0);
        if (t <= 0.0) return isOpening ? 1.0 : 0.0;
        if (t >= 1.0) return isOpening ? 0.0 : 1.0;

        return style switch
        {
            ContainerAnimationStyle.Genie or ContainerAnimationStyle.MagicLamp => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 2.55))
                : Math.Clamp(0.32 * t + 0.68 * (t * t), 0.0, 1.0),

            ContainerAnimationStyle.Suck or ContainerAnimationStyle.BlackHole => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 2.75))
                : Math.Clamp(0.28 * t + 0.72 * (t * t), 0.0, 1.0),

            ContainerAnimationStyle.Origami or ContainerAnimationStyle.PageCurl => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 2.5))
                : Math.Clamp(0.30 * t + 0.70 * (t * t), 0.0, 1.0),

            ContainerAnimationStyle.Flip3D => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 2.8))
                : Math.Clamp(t * t, 0.0, 1.0),

            ContainerAnimationStyle.Jelly => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 2.2))
                : Math.Clamp(0.25 * t + 0.75 * (t * t), 0.0, 1.0),

            ContainerAnimationStyle.Scale => isOpening
                ? 1.0 - (1.0 - Math.Pow(1.0 - t, 3.1))
                : Math.Clamp(t * t, 0.0, 1.0),

            _ => isOpening ? 1.0 - t : t
        };
    }

    public static double ComputeOpacity(ContainerAnimationStyle style, double collapseProgress)
    {
        double p = Math.Clamp(collapseProgress, 0.0, 1.0);
        if (p <= 0.0) return 1.0;
        if (p >= 1.0) return 0.0;

        if (style is ContainerAnimationStyle.Scale or ContainerAnimationStyle.Flip3D)
        {
            if (p <= 0.20) return 1.0;
            return Math.Clamp(1.0 - (p - 0.20) / 0.80, 0.0, 1.0);
        }

        // Mesh-warped styles stay opaque through the deform/pour and fade only the final tip entering the icon
        if (p <= 0.86) return 1.0;
        return Math.Clamp(1.0 - (p - 0.86) / 0.14, 0.0, 1.0);
    }

    public static WarpVertex Evaluate(
        ContainerAnimationStyle style,
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double collapseProgress)
    {
        u = Math.Clamp(u, 0.0, 1.0);
        v = Math.Clamp(v, 0.0, 1.0);
        anchorU = Math.Clamp(anchorU, 0.02, 0.98);
        anchorV = Math.Clamp(anchorV, 0.02, 0.98);
        double p = Math.Clamp(collapseProgress, 0.0, 1.0);

        if (p <= 1e-6)
        {
            return new WarpVertex(u * width, v * height, 0.0, u, v);
        }

        return style switch
        {
            ContainerAnimationStyle.Genie => EvaluateGenie(u, v, width, height, anchorU, anchorV, lateralEdge, p, waveStrength: 0.0),
            ContainerAnimationStyle.MagicLamp => EvaluateGenie(u, v, width, height, anchorU, anchorV, lateralEdge, p, waveStrength: 0.135),
            ContainerAnimationStyle.Suck => EvaluateSuck(u, v, width, height, anchorU, anchorV, lateralEdge, p),
            ContainerAnimationStyle.Origami => EvaluateOrigami(u, v, width, height, anchorU, anchorV, lateralEdge, p),
            ContainerAnimationStyle.PageCurl => EvaluatePageCurl(u, v, width, height, anchorU, anchorV, lateralEdge, p),
            ContainerAnimationStyle.BlackHole => EvaluateBlackHole(u, v, width, height, anchorU, anchorV, p),
            ContainerAnimationStyle.Flip3D => EvaluateFlip3D(u, v, width, height, anchorU, anchorV, lateralEdge, p),
            ContainerAnimationStyle.Jelly => EvaluateJelly(u, v, width, height, anchorU, anchorV, lateralEdge, p),
            _ => EvaluateScale(u, v, width, height, anchorU, anchorV, p)
        };
    }

    private static WarpVertex EvaluateGenie(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p,
        double waveStrength)
    {
        double neckPhase = SmoothStep(0.0, 0.48, p);
        double pourPhase = SmoothStep(0.14, 1.0, p);
        double tailPinch = SmoothStep(0.68, 1.0, pourPhase);
        double neckHalfSpan = Lerp(0.056, 0.005, tailPinch);

        double xNorm;
        double yNorm;
        double zDepth;

        if (lateralEdge is { } edge)
        {
            double distToEdgeX = edge == ScreenEdge.Right ? u : (1.0 - u);
            double colStretch = (1.0 - distToEdgeX) * 0.14 * Math.Sin(Math.PI * pourPhase);
            double effectivePour = Math.Clamp(pourPhase - colStretch, 0.0, 1.0);

            xNorm = Lerp(u, anchorU, effectivePour);
            double currentProgressToEdge = edge == ScreenEdge.Right
                ? (anchorU > 0.01 ? Math.Clamp(xNorm / anchorU, 0.0, 1.0) : 1.0)
                : (anchorU < 0.99 ? Math.Clamp((1.0 - xNorm) / (1.0 - anchorU), 0.0, 1.0) : 1.0);

            double sCurve = CosineSCurve(Math.Pow(currentProgressToEdge, 1.15));
            double pinch = Math.Clamp(neckPhase * sCurve, 0.0, 1.0);

            double wave = waveStrength > 0
                ? Math.Sin(currentProgressToEdge * Math.PI * 2.5 - p * Math.PI * 3.0) * waveStrength * Math.Sin(Math.PI * currentProgressToEdge) * Math.Sin(Math.PI * p)
                : 0.0;

            double topBound = Lerp(0.0, Math.Max(0.0, anchorV - neckHalfSpan), pinch) + wave;
            double bottomBound = Lerp(1.0, Math.Min(1.0, anchorV + neckHalfSpan), pinch) + wave;
            yNorm = Math.Clamp(Lerp(topBound, bottomBound, v), 0.0, 1.0);

            zDepth = Math.Sin(Math.PI * v) * Math.Sin(Math.PI * currentProgressToEdge) * Math.Sin(Math.PI * p) * (height * 0.075);
        }
        else
        {
            double distToAnchorY = 1.0 - Math.Abs(anchorV - v);
            double rowStretch = (1.0 - distToAnchorY) * 0.14 * Math.Sin(Math.PI * pourPhase);
            double effectivePour = Math.Clamp(pourPhase - rowStretch, 0.0, 1.0);

            yNorm = Lerp(v, anchorV, effectivePour);
            double currentProgressToAnchor = anchorV >= 0.5
                ? Math.Clamp(yNorm / Math.Max(0.01, anchorV), 0.0, 1.0)
                : Math.Clamp((1.0 - yNorm) / Math.Max(0.01, 1.0 - anchorV), 0.0, 1.0);

            double sCurve = CosineSCurve(Math.Pow(currentProgressToAnchor, 1.15));
            double pinch = Math.Clamp(neckPhase * sCurve, 0.0, 1.0);

            double wave = waveStrength > 0
                ? Math.Sin(currentProgressToAnchor * Math.PI * 2.5 - p * Math.PI * 3.0) * waveStrength * Math.Sin(Math.PI * currentProgressToAnchor) * Math.Sin(Math.PI * p)
                : 0.0;

            double leftBound = Lerp(0.0, Math.Max(0.0, anchorU - neckHalfSpan), pinch) + wave;
            double rightBound = Lerp(1.0, Math.Min(1.0, anchorU + neckHalfSpan), pinch) + wave;
            xNorm = Math.Clamp(Lerp(leftBound, rightBound, u), 0.0, 1.0);

            zDepth = Math.Sin(Math.PI * u) * Math.Sin(Math.PI * currentProgressToAnchor) * Math.Sin(Math.PI * p) * (width * 0.075);
        }

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluateSuck(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p)
    {
        double dx = u - anchorU;
        double dy = v - anchorV;
        double rawDist = Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(2.0);

        double cornerBias = lateralEdge is null ? (u * 0.18) : (v * 0.18);
        double vertexDelay = Math.Clamp(rawDist * 0.48 + cornerBias * 0.11, 0.0, 0.56);
        double localP = SmoothStep(vertexDelay, vertexDelay + 0.44, p);

        double radialPull = Math.Pow(localP, 1.32);
        double concavePinch = Math.Clamp(
            Math.Pow(Math.Sin(Math.PI * localP), 0.82) * 0.50 + radialPull,
            0.0,
            1.0);

        double xNorm;
        double yNorm;
        double swoop = Math.Sin(Math.PI * localP) * 0.085;

        if (lateralEdge is not null)
        {
            xNorm = Lerp(u, anchorU, radialPull) + Math.Sin(Math.PI * v) * swoop * (anchorU - u);
            yNorm = Lerp(v, anchorV, concavePinch);
        }
        else
        {
            xNorm = Lerp(u, anchorU, concavePinch);
            yNorm = Lerp(v, anchorV, radialPull) + Math.Sin(Math.PI * u) * swoop * (anchorV - v);
        }

        xNorm = Math.Clamp(xNorm, 0.0, 1.0);
        yNorm = Math.Clamp(yNorm, 0.0, 1.0);

        double zDepth = -Math.Sin(Math.PI * u) * Math.Sin(Math.PI * v) * Math.Sin(Math.PI * localP) * (Math.Min(width, height) * 0.09);

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluateOrigami(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p)
    {
        const double pleats = 5.0;
        double foldPhase = Math.Sin(Math.PI * p);
        double compress = SmoothStep(0.0, 1.0, p);

        double primaryCoord = lateralEdge is not null ? u : v;
        double triangleWave = 1.0 - 2.0 * Math.Abs(((primaryCoord * pleats) % 2.0) - 1.0);
        double zDepth = triangleWave * foldPhase * (Math.Min(width, height) * 0.13);

        // Each accordion ridge pinches slightly inward as it folds
        double ridgePinch = (triangleWave * 0.5 + 0.5) * foldPhase * 0.14;
        double fanPinch = Math.Clamp(compress + ridgePinch, 0.0, 1.0);

        double xNorm = Lerp(u, anchorU, lateralEdge is not null ? compress : fanPinch);
        double yNorm = Lerp(v, anchorV, lateralEdge is not null ? fanPinch : compress);

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluatePageCurl(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p)
    {
        double s = SmoothStep(0.0, 1.0, p);
        double curlAngle = s * Math.PI * 0.85;

        double xNorm;
        double yNorm;
        double zDepth;

        if (lateralEdge is not null)
        {
            double distFromAnchor = Math.Abs(u - anchorU);
            double localCurl = curlAngle * Math.Pow(distFromAnchor, 0.85);
            double wrappedDist = distFromAnchor * Math.Cos(localCurl * 0.65) * (1.0 - s);
            xNorm = anchorU + Math.Sign(u - anchorU) * wrappedDist;
            double vPinch = SmoothStep(distFromAnchor * 0.35, 1.0, p);
            yNorm = Lerp(v, anchorV, vPinch);
            zDepth = Math.Sin(localCurl) * Math.Sin(Math.PI * p) * (width * 0.16);
        }
        else
        {
            double distFromAnchor = Math.Abs(v - anchorV);
            double localCurl = curlAngle * Math.Pow(distFromAnchor, 0.85);
            double wrappedDist = distFromAnchor * Math.Cos(localCurl * 0.65) * (1.0 - s);
            yNorm = anchorV + Math.Sign(v - anchorV) * wrappedDist;
            double uPinch = SmoothStep(distFromAnchor * 0.35, 1.0, p);
            xNorm = Lerp(u, anchorU, uPinch);
            zDepth = Math.Sin(localCurl) * Math.Sin(Math.PI * p) * (height * 0.16);
        }

        return new WarpVertex(
            Math.Clamp(xNorm, 0.0, 1.0) * width,
            Math.Clamp(yNorm, 0.0, 1.0) * height,
            zDepth,
            u,
            v);
    }

    private static WarpVertex EvaluateBlackHole(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        double p)
    {
        double dx = u - anchorU;
        double dy = v - anchorV;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double s = SmoothStep(0.0, 1.0, p);

        // Spiral angle increases closer to the singularity
        double swirlAngle = s * 2.8 * Math.Pow(Math.Max(0.0, 1.15 - dist), 0.9);
        double cosA = Math.Cos(swirlAngle);
        double sinA = Math.Sin(swirlAngle);

        double shrink = 1.0 - Math.Pow(s, 1.15);
        double rotDx = (dx * cosA - dy * sinA) * shrink;
        double rotDy = (dx * sinA + dy * cosA) * shrink;

        double xNorm = Math.Clamp(anchorU + rotDx, 0.0, 1.0);
        double yNorm = Math.Clamp(anchorV + rotDy, 0.0, 1.0);
        double zDepth = -Math.Sin(Math.PI * p) * Math.Exp(-dist * 2.2) * (Math.Min(width, height) * 0.18);

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluateFlip3D(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p)
    {
        double s = SmoothStep(0.0, 1.0, p);
        double tiltAngle = s * (Math.PI * 0.42); // ~75 degrees perspective swing
        double scale = Lerp(1.0, 0.06, s);

        double xNorm = anchorU + (u - anchorU) * scale;
        double yNorm = anchorV + (v - anchorV) * scale;
        double zDepth;

        if (lateralEdge is not null)
        {
            double arm = Math.Abs(u - anchorU);
            xNorm = anchorU + (u - anchorU) * scale * Math.Cos(tiltAngle);
            zDepth = -arm * Math.Sin(tiltAngle) * (width * 0.45);
        }
        else
        {
            double arm = Math.Abs(v - anchorV);
            yNorm = anchorV + (v - anchorV) * scale * Math.Cos(tiltAngle);
            zDepth = -arm * Math.Sin(tiltAngle) * (height * 0.45);
        }

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluateJelly(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double p)
    {
        double dx = u - anchorU;
        double dy = v - anchorV;
        double dist = Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(2.0);

        // Trailing vertices lag and oscillate with a damped harmonic wave that settles to 0 at p=0 and p=1
        double springWobble = Math.Sin(p * Math.PI * 3.0) * Math.Sin(Math.PI * p) * dist * 0.22;
        double scale = Math.Clamp(Lerp(1.0, 0.04, SmoothStep(0.0, 1.0, p)) - springWobble, 0.02, 1.18);

        double xStretch = lateralEdge is null ? (1.0 + springWobble * 0.45) : (1.0 - springWobble * 0.35);
        double yStretch = lateralEdge is null ? (1.0 - springWobble * 0.35) : (1.0 + springWobble * 0.45);

        double xNorm = anchorU + dx * scale * xStretch;
        double yNorm = anchorV + dy * scale * yStretch;
        double zDepth = Math.Sin(Math.PI * u) * Math.Sin(Math.PI * v) * springWobble * (Math.Min(width, height) * 0.35);

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static WarpVertex EvaluateScale(
        double u,
        double v,
        double width,
        double height,
        double anchorU,
        double anchorV,
        double p)
    {
        double s = SmoothStep(0.0, 1.0, p);
        double scale = Lerp(1.0, 0.04, s);
        double xNorm = anchorU + (u - anchorU) * scale;
        double yNorm = anchorV + (v - anchorV) * scale;
        double zDepth = -s * (Math.Min(width, height) * 0.04);

        return new WarpVertex(xNorm * width, yNorm * height, zDepth, u, v);
    }

    private static double CosineSCurve(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        return 0.5 - 0.5 * Math.Cos(Math.PI * t);
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        if (edge1 <= edge0) return x >= edge0 ? 1.0 : 0.0;
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3.0 - 2.0 * t);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    /// Computes macOS Dock Gaussian bell-curve magnification scale for an icon at <paramref name="distanceDip"/>
    /// from the cursor within <paramref name="influenceRadiusDip"/>.
    /// </summary>
    public static double ComputeDockMagnificationScale(double distanceDip, double influenceRadiusDip = 105.0, double maxExtraScale = 0.32)
    {
        if (influenceRadiusDip <= 1.0) return 1.0;
        double d = Math.Abs(distanceDip);
        if (d >= influenceRadiusDip) return 1.0;
        double normalized = d / influenceRadiusDip;
        // Smooth cosine bell window that reaches 1.0 at d=0 and 0.0 with zero derivative at d=influenceRadiusDip
        double bell = 0.5 * (1.0 + Math.Cos(Math.PI * normalized));
        return 1.0 + maxExtraScale * (bell * bell);
    }
}
