using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using AppBox.Core;

namespace AppBox.App.Services;

/// <summary>
/// Hardware-accelerated 3-D mesh renderer for macOS-style Genie, Suck, and Scale animations.
/// Captures the target visual frame-perfectly into a premultiplied Pbgra32 texture so there is no black flash.
/// </summary>
public sealed class WarpMeshRenderer
{
    public const int GridColumns = 32;
    public const int GridRows = 32;

    private readonly Viewport3D viewport;
    private readonly PerspectiveCamera camera;
    private readonly GeometryModel3D geometryModel;
    private readonly MeshGeometry3D mesh;
    private readonly Point3DCollection positionsBuffer;

    public WarpMeshRenderer(
        Viewport3D viewport,
        PerspectiveCamera camera,
        GeometryModel3D geometryModel,
        MeshGeometry3D mesh)
    {
        this.viewport = viewport;
        this.camera = camera;
        this.geometryModel = geometryModel;
        this.mesh = mesh;

        int vertexCount = (GridColumns + 1) * (GridRows + 1);
        positionsBuffer = new Point3DCollection(vertexCount);
        var texCoords = new PointCollection(vertexCount);

        for (int r = 0; r <= GridRows; r++)
        {
            double v = (double)r / GridRows;
            for (int c = 0; c <= GridColumns; c++)
            {
                double u = (double)c / GridColumns;
                positionsBuffer.Add(new Point3D(0, 0, 0));
                texCoords.Add(new Point(u, v));
            }
        }

        int stride = GridColumns + 1;
        var indices = new Int32Collection(GridColumns * GridRows * 6);
        for (int r = 0; r < GridRows; r++)
        {
            for (int c = 0; c < GridColumns; c++)
            {
                int tl = r * stride + c;
                int tr = tl + 1;
                int bl = (r + 1) * stride + c;
                int br = bl + 1;

                // Counter-clockwise winding facing +Z
                indices.Add(tl);
                indices.Add(bl);
                indices.Add(br);

                indices.Add(tl);
                indices.Add(br);
                indices.Add(tr);
            }
        }

        texCoords.Freeze();
        indices.Freeze();
        mesh.TextureCoordinates = texCoords;
        mesh.TriangleIndices = indices;
        mesh.Positions = positionsBuffer;
    }

    public bool CaptureAndBindMaterial(Visual targetVisual, double width, double height)
    {
        if (width < 4 || height < 4) return false;
        var dpi = VisualTreeHelper.GetDpi(targetVisual);
        int pxW = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX));
        int pxH = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY));

        var rtb = new RenderTargetBitmap(pxW, pxH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(targetVisual);
        rtb.Freeze();

        var brush = new ImageBrush(rtb)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1, 1)
        };
        brush.Freeze();

        var material = new DiffuseMaterial(brush);
        material.Freeze();
        geometryModel.Material = material;
        geometryModel.BackMaterial = material;
        ConfigureCamera(width, height);
        return true;
    }

    public void ConfigureCamera(double width, double height)
    {
        double halfW = width / 2.0;
        double halfH = height / 2.0;
        const double fovDeg = 45.0;
        double fovRad = fovDeg * Math.PI / 180.0;
        double camZ = halfW / Math.Tan(fovRad / 2.0);

        camera.Position = new Point3D(halfW, halfH, camZ);
        camera.LookDirection = new Vector3D(0, 0, -1);
        camera.UpDirection = new Vector3D(0, 1, 0);
        camera.FieldOfView = fovDeg;
        camera.NearPlaneDistance = Math.Max(1.0, camZ * 0.1);
        camera.FarPlaneDistance = Math.Max(2000.0, camZ * 4.0);
    }

    public void UpdatePositions(
        ContainerAnimationStyle style,
        double width,
        double height,
        double anchorU,
        double anchorV,
        ScreenEdge? lateralEdge,
        double collapseProgress)
    {
        mesh.Positions = null;
        int idx = 0;
        for (int r = 0; r <= GridRows; r++)
        {
            double v = (double)r / GridRows;
            for (int c = 0; c <= GridColumns; c++)
            {
                double u = (double)c / GridColumns;
                var vert = ContainerWarpMesh.Evaluate(style, u, v, width, height, anchorU, anchorV, lateralEdge, collapseProgress);
                positionsBuffer[idx++] = new Point3D(vert.X, height - vert.Y, vert.Z);
            }
        }
        mesh.Positions = positionsBuffer;
        viewport.Opacity = ContainerWarpMesh.ComputeOpacity(style, collapseProgress);
    }
}
