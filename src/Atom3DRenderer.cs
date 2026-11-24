using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AntigravityAtomTokenWidget
{
    internal sealed class ElectricArcBinding
    {
        public AxisAngleRotation3D ZSpin;
        public double BaseDeg;
        public double SpeedDegPerSec;
    }

    internal sealed class OrbitTrack
    {
        public double SemiMajor;
        public double SemiMinor;
        public Matrix3D Rotation;
        public double SpeedRadPerSec;
        public List<double> ElectronPhases = new List<double>();
        public List<TranslateTransform3D> ElectronTransforms = new List<TranslateTransform3D>();
        public List<ElectricArcBinding> Arcs = new List<ElectricArcBinding>();
    }

    public sealed class Atom3DControl : Grid
    {
        private readonly Viewport3D viewport;
        private readonly ModelVisual3D sceneRoot;
        private readonly Model3DGroup atomGroup;
        private readonly ScaleTransform3D atomScaleTransform;
        private readonly RotateTransform3D atomYawTransform;
        private readonly AxisAngleRotation3D atomYawAxis;
        private readonly RotateTransform3D atomPitchTransform;
        private readonly AxisAngleRotation3D atomPitchAxis;
        private readonly TranslateTransform3D atomBobTransform;

        private readonly AmbientLight ambientLight;
        private readonly List<OrbitTrack> tracks = new List<OrbitTrack>();
        private readonly List<SolidColorBrush> arcPulseBrushes = new List<SolidColorBrush>();

        private AtomElement currentElement = AtomElement.Neon;
        private bool currentOrchestrator = false;
        private bool currentLightTheme = false;
        private bool isRunning = false;
        private bool animationEnabled = true;

        private double simTime = 0.52;
        private double speedMultiplier = 1.0;
        private double transitionProgress = 1.0;

        private static readonly MeshGeometry3D SharedSphereMesh = BuildSphereMesh(1.0, 18, 24);

        public Atom3DControl()
        {
            this.Background = Brushes.Transparent;
            this.IsHitTestVisible = true;
            this.ClipToBounds = false;

            viewport = new Viewport3D();
            viewport.ClipToBounds = false;
            viewport.IsHitTestVisible = false;

            PerspectiveCamera cam = new PerspectiveCamera();
            cam.Position = new Point3D(0.0, 0.0, 5.35);
            cam.LookDirection = new Vector3D(0.0, 0.0, -1.0);
            cam.UpDirection = new Vector3D(0.0, 1.0, 0.0);
            cam.FieldOfView = 36.0;
            viewport.Camera = cam;

            Model3DGroup lights = new Model3DGroup();
            ambientLight = new AmbientLight(Color.FromRgb(88, 96, 112));
            DirectionalLight keyLight = new DirectionalLight(Color.FromRgb(255, 253, 248), new Vector3D(-0.55, -0.70, -0.85));
            DirectionalLight rimLight = new DirectionalLight(Color.FromRgb(200, 222, 250), new Vector3D(0.75, 0.45, 0.55));
            lights.Children.Add(ambientLight);
            lights.Children.Add(keyLight);
            lights.Children.Add(rimLight);

            ModelVisual3D lightVisual = new ModelVisual3D();
            lightVisual.Content = lights;
            viewport.Children.Add(lightVisual);

            atomGroup = new Model3DGroup();
            atomScaleTransform = new ScaleTransform3D(1.0, 1.0, 1.0);
            atomYawAxis = new AxisAngleRotation3D(new Vector3D(0.18, 1.0, 0.10), 0.0);
            atomYawTransform = new RotateTransform3D(atomYawAxis);
            atomPitchAxis = new AxisAngleRotation3D(new Vector3D(1.0, 0.0, 0.0), 8.0);
            atomPitchTransform = new RotateTransform3D(atomPitchAxis);
            atomBobTransform = new TranslateTransform3D(0.0, 0.0, 0.0);

            Transform3DGroup tg = new Transform3DGroup();
            tg.Children.Add(atomScaleTransform);
            tg.Children.Add(atomPitchTransform);
            tg.Children.Add(atomYawTransform);
            tg.Children.Add(atomBobTransform);
            atomGroup.Transform = tg;

            sceneRoot = new ModelVisual3D();
            sceneRoot.Content = atomGroup;
            viewport.Children.Add(sceneRoot);

            this.Children.Add(viewport);
            RebuildAtom(AtomElement.Neon, false, false, false);
            ApplyPoseAtCurrentSimTime();
        }

        public AtomElement Element
        {
            get { return currentElement; }
        }

        public bool IsOrchestrator
        {
            get { return currentOrchestrator; }
        }

        public bool AnimationEnabled
        {
            get { return animationEnabled; }
        }

        public int DebugChildModelCount
        {
            get { return atomGroup.Children.Count; }
        }

        public int DebugTotalVertexCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < atomGroup.Children.Count; i++)
                {
                    GeometryModel3D gm = atomGroup.Children[i] as GeometryModel3D;
                    if (gm != null)
                    {
                        MeshGeometry3D mg = gm.Geometry as MeshGeometry3D;
                        if (mg != null)
                        {
                            total += mg.Positions.Count;
                        }
                    }
                }
                return total;
            }
        }

        public void SetState(AtomElement element, bool orchestrator, bool lightTheme, bool running)
        {
            SetState(element, orchestrator, lightTheme, running, this.animationEnabled);
        }

        public void SetState(AtomElement element, bool orchestrator, bool lightTheme, bool running, bool enableAnim)
        {
            this.isRunning = running;
            bool animChanged = (this.animationEnabled != enableAnim);
            this.animationEnabled = enableAnim;

            bool needRebuild = (element != currentElement) || (orchestrator != currentOrchestrator) || (lightTheme != currentLightTheme);
            if (needRebuild)
            {
                bool animateSwitch = enableAnim && ((element != currentElement) || (orchestrator != currentOrchestrator));
                RebuildAtom(element, orchestrator, lightTheme, animateSwitch);
                if (!enableAnim)
                {
                    ApplyStaticPose();
                }
                else
                {
                    ApplyPoseAtCurrentSimTime();
                }
            }
            else if (animChanged && !enableAnim)
            {
                ApplyStaticPose();
            }
        }

        public void ApplyStaticPose()
        {
            simTime = 0.52;
            transitionProgress = 1.0;
            speedMultiplier = 1.0;
            ApplyPoseAtCurrentSimTime();
            atomBobTransform.OffsetY = 0.0;
        }

        public void StepAnimation(double dt)
        {
            if (!animationEnabled)
            {
                return;
            }
            if (dt <= 0.0 || dt > 0.1)
            {
                dt = 0.016;
            }

            double targetSpeed = (currentElement == AtomElement.Unsupported) ? 0.28 : (isRunning ? 2.35 : 1.0);
            speedMultiplier += (targetSpeed - speedMultiplier) * Math.Min(1.0, dt * 6.0);
            simTime += dt * speedMultiplier;

            if (transitionProgress < 1.0)
            {
                transitionProgress = Math.Min(1.0, transitionProgress + dt / 0.22);
            }
            ApplyPoseAtCurrentSimTime();
        }

        private void ApplyPoseAtCurrentSimTime()
        {
            double easeScale = 1.0 - Math.Pow(1.0 - transitionProgress, 3.0);
            double s = animationEnabled ? (0.86 + 0.14 * easeScale) : 1.0;
            atomScaleTransform.ScaleX = s;
            atomScaleTransform.ScaleY = s;
            atomScaleTransform.ScaleZ = s;

            atomYawAxis.Angle = Math.Sin(simTime * 0.42) * 18.0 + simTime * 7.5;
            atomPitchAxis.Angle = 8.0 + Math.Cos(simTime * 0.35) * 6.0;
            atomBobTransform.OffsetY = animationEnabled ? (Math.Sin(simTime * 1.35) * 0.045) : 0.0;

            for (int i = 0; i < tracks.Count; i++)
            {
                OrbitTrack tr = tracks[i];
                for (int j = 0; j < tr.ElectronTransforms.Count; j++)
                {
                    double theta = tr.ElectronPhases[j] + simTime * tr.SpeedRadPerSec;
                    Point3D localPt = new Point3D(tr.SemiMajor * Math.Cos(theta), tr.SemiMinor * Math.Sin(theta), 0.0);
                    Point3D worldPt = tr.Rotation.Transform(localPt);
                    TranslateTransform3D tt = tr.ElectronTransforms[j];
                    tt.OffsetX = worldPt.X;
                    tt.OffsetY = worldPt.Y;
                    tt.OffsetZ = worldPt.Z;
                }
                for (int k = 0; k < tr.Arcs.Count; k++)
                {
                    ElectricArcBinding arc = tr.Arcs[k];
                    arc.ZSpin.Angle = arc.BaseDeg + simTime * arc.SpeedDegPerSec;
                }
            }

            if (arcPulseBrushes.Count > 0)
            {
                double shimmer = animationEnabled ? (0.82 + 0.18 * Math.Sin(simTime * 5.2)) : 0.88;
                byte a = (byte)Math.Max(90, Math.Min(168, (int)Math.Round(152.0 * shimmer)));
                for (int i = 0; i < arcPulseBrushes.Count; i++)
                {
                    Color c = arcPulseBrushes[i].Color;
                    arcPulseBrushes[i].Color = Color.FromArgb(a, c.R, c.G, c.B);
                }
            }

            if (currentElement == AtomElement.Unsupported)
            {
                this.Opacity = currentLightTheme ? 0.88 : 0.84;
            }
            else
            {
                this.Opacity = animationEnabled ? (0.72 + 0.28 * easeScale) : 1.0;
            }
        }

        private void RebuildAtom(AtomElement element, bool orchestrator, bool lightTheme, bool animateSwitch)
        {
            currentElement = element;
            currentOrchestrator = orchestrator;
            currentLightTheme = lightTheme;
            transitionProgress = animateSwitch ? 0.0 : 1.0;

            tracks.Clear();
            arcPulseBrushes.Clear();
            atomGroup.Children.Clear();

            ambientLight.Color = lightTheme ? Color.FromRgb(106, 114, 130) : Color.FromRgb(86, 94, 112);

            ElementVisualInfo vinfo = ElementVisualCatalog.GetInfo(element);
            Color accent = lightTheme ? vinfo.LightAccent : vinfo.DarkAccent;
            bool unsupported = (element == AtomElement.Unsupported);

            Material protonMat;
            Material neutronMat;
            Material electronMat;
            Material orbitMat;
            Material electricArcMat = null;

            if (unsupported)
            {
                // Hollow dashed 3D wireframe materials with crisp contrast on both Light and Dark themes
                neutronMat = MakeGlossyMaterial(
                    lightTheme ? Color.FromRgb(86, 102, 128) : Color.FromRgb(168, 184, 208),
                    lightTheme ? Color.FromRgb(22, 28, 38) : Color.FromRgb(56, 66, 82),
                    60.0, 245);
                protonMat = neutronMat;
                electronMat = MakeGlossyMaterial(
                    lightTheme ? Color.FromRgb(72, 82, 98) : Color.FromRgb(206, 214, 226),
                    lightTheme ? Color.FromRgb(18, 22, 28) : Color.FromRgb(76, 82, 92),
                    72.0, 250);
                orbitMat = MakeGlossyMaterial(
                    lightTheme ? Color.FromRgb(106, 118, 136) : Color.FromRgb(156, 168, 188),
                    lightTheme ? Color.FromRgb(24, 28, 36) : Color.FromRgb(44, 52, 66),
                    48.0, 230);
            }
            else if (orchestrator)
            {
                // Orchestrator: theme-adaptive Metallic Gold protons, Silver neutrons, Copper electrons
                // plus element-harmonized cool metallic orbits and subtle electric blue current filaments (no outer halo ring).
                if (lightTheme)
                {
                    protonMat = MakeMetallicMaterial(
                        Color.FromRgb(202, 148, 36),   // Burnished Gold (high contrast on light bg)
                        Color.FromRgb(255, 244, 198),
                        Color.FromRgb(48, 30, 4),
                        108.0, 255);

                    neutronMat = MakeMetallicMaterial(
                        Color.FromRgb(152, 165, 184),  // Crisp Sterling Silver
                        Color.FromRgb(245, 250, 255),
                        Color.FromRgb(28, 34, 46),
                        118.0, 255);

                    electronMat = MakeMetallicMaterial(
                        Color.FromRgb(182, 92, 48),    // Deep Burnished Copper
                        Color.FromRgb(255, 218, 194),
                        Color.FromRgb(54, 22, 8),
                        104.0, 255);
                }
                else
                {
                    protonMat = MakeMetallicMaterial(
                        Color.FromRgb(228, 182, 76),   // Polished Metallic Gold
                        Color.FromRgb(255, 246, 214),
                        Color.FromRgb(58, 38, 6),
                        115.0, 255);

                    neutronMat = MakeMetallicMaterial(
                        Color.FromRgb(205, 213, 224),  // Brushed Metallic Silver
                        Color.FromRgb(255, 255, 255),
                        Color.FromRgb(34, 40, 50),
                        125.0, 255);

                    electronMat = MakeMetallicMaterial(
                        Color.FromRgb(204, 120, 76),   // Polished Metallic Copper
                        Color.FromRgb(255, 224, 204),
                        Color.FromRgb(61, 29, 13),
                        110.0, 255);
                }

                Color orbitBase = lightTheme ? Color.FromRgb(84, 98, 126) : Color.FromRgb(138, 156, 186);
                Color orbitElem = BlendColor(orbitBase, accent, 0.16);
                Color orbitTint = BlendColor(orbitElem, lightTheme ? Color.FromRgb(38, 128, 232) : Color.FromRgb(86, 168, 255), 0.24);
                orbitMat = MakeGlossyMaterial(
                    orbitTint,
                    lightTheme ? Color.FromRgb(8, 22, 44) : Color.FromRgb(12, 28, 52),
                    68.0,
                    lightTheme ? (byte)245 : (byte)240);

                // Subtle electric blue current material (theme-adapted contrast, kept unfrozen for gentle shimmer)
                SolidColorBrush arcDiffBrush = new SolidColorBrush(lightTheme
                    ? Color.FromArgb(165, 28, 124, 238)
                    : Color.FromArgb(145, 76, 178, 255));
                SolidColorBrush arcEmBrush = new SolidColorBrush(lightTheme
                    ? Color.FromArgb(125, 22, 108, 218)
                    : Color.FromArgb(145, 58, 152, 245));
                arcPulseBrushes.Add(arcDiffBrush);
                arcPulseBrushes.Add(arcEmBrush);
                MaterialGroup arcGroup = new MaterialGroup();
                arcGroup.Children.Add(new DiffuseMaterial(arcDiffBrush));
                arcGroup.Children.Add(new EmissiveMaterial(arcEmBrush));
                electricArcMat = arcGroup;
            }
            else
            {
                // Standard Gemini 4: Red protons, Blue neutrons, Charcoal-Silver electrons + AI Studio accent-tinted orbits
                protonMat = MakeGlossyMaterial(Color.FromRgb(230, 52, 46), Color.FromRgb(42, 8, 8), 72.0, 255);
                neutronMat = MakeGlossyMaterial(Color.FromRgb(28, 82, 192), Color.FromRgb(8, 20, 48), 72.0, 255);

                Color electronDiffuse = lightTheme ? Color.FromRgb(86, 94, 108) : Color.FromRgb(124, 136, 156);
                Color electronEmissive = Color.FromRgb((byte)(accent.R * 0.22), (byte)(accent.G * 0.22), (byte)(accent.B * 0.22));
                electronMat = MakeGlossyMaterial(electronDiffuse, electronEmissive, 96.0, 255);

                Color orbitBase = lightTheme ? Color.FromRgb(94, 102, 116) : Color.FromRgb(148, 158, 174);
                Color orbitTint = BlendColor(orbitBase, accent, 0.22);
                orbitMat = MakeGlossyMaterial(orbitTint, Color.FromRgb((byte)(accent.R * 0.11), (byte)(accent.G * 0.11), (byte)(accent.B * 0.11)), 54.0, 240);
            }

            if (element == AtomElement.Unsupported)
            {
                // Hollow dashed spherical wireframe neutron/nucleus (transparent open center)
                MeshGeometry3D hollowNucleusMesh = BuildDashedHollowSphereMesh(0.32, 0.025, 8, 0.52, 4, 8);
                GeometryModel3D nucModel = new GeometryModel3D(hollowNucleusMesh, neutronMat);
                nucModel.Transform = new MatrixTransform3D(BuildEulerMatrix(22.0, -28.0, 14.0));
                atomGroup.Children.Add(nucModel);

                // Dashed 3D elliptical orbit + hollow dashed orbiting electron
                AddDashedOrbitWithHollowElectrons(
                    1.16, 0.60, 0.027,
                    18, 0.52,
                    32.0, 12.0, -20.0,
                    1, 0.175, 0.019, 4, 0.56,
                    1.0, orbitMat, electronMat);
                return;
            }

            if (element == AtomElement.Hydrogen)
            {
                AddNucleonSphere(0.0, 0.0, 0.0, 0.24, protonMat);
                AddOrbitWithElectrons(1.18, 0.62, 0.024, 34.0, 16.0, -22.0, 1, 0.145, 2.0, orbitMat, electronMat, electricArcMat, orchestrator ? 2 : 0);
            }
            else if (element == AtomElement.Carbon)
            {
                AddNucleusCluster(6, 6, 0.112, 0.155, protonMat, neutronMat);
                AddOrbitWithElectrons(0.76, 0.44, 0.021, -34.0, 12.0, 8.0, 2, 0.105, 2.5, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                AddOrbitWithElectrons(1.23, 0.64, 0.022, 34.0, 10.0, 56.0, 2, 0.125, 1.85, orbitMat, electronMat, electricArcMat, orchestrator ? 2 : 0);
                AddOrbitWithElectrons(1.23, 0.64, 0.022, 34.0, -10.0, -56.0, 2, 0.125, -1.75, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
            }
            else if (element == AtomElement.Neon)
            {
                AddNucleusCluster(10, 10, 0.096, 0.175, protonMat, neutronMat);
                AddOrbitWithElectrons(0.72, 0.43, 0.019, -32.0, 14.0, 18.0, 2, 0.092, 2.6, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                double[] rolls = new double[] { 12.0, 57.0, 102.0, 147.0 };
                double[] xTilts = new double[] { 34.0, -34.0, 36.0, -36.0 };
                for (int i = 0; i < 4; i++)
                {
                    double spd = (i % 2 == 0) ? 1.85 : -1.75;
                    AddOrbitWithElectrons(1.24, 0.62, 0.018, xTilts[i], (i - 1.5) * 6.0, rolls[i], 2, 0.102, spd, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                }
            }
            else if (element == AtomElement.Argon)
            {
                AddNucleusCluster(18, 22, 0.082, 0.195, protonMat, neutronMat);
                AddOrbitWithElectrons(0.60, 0.36, 0.016, -32.0, 10.0, 15.0, 2, 0.075, 2.8, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                AddOrbitWithElectrons(0.93, 0.50, 0.016, 34.0, 12.0, 28.0, 4, 0.082, 2.1, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                AddOrbitWithElectrons(0.93, 0.50, 0.016, -34.0, -12.0, 118.0, 4, 0.082, -2.0, orbitMat, electronMat, electricArcMat, orchestrator ? 1 : 0);
                double[] rolls = new double[] { 0.0, 45.0, 90.0, 135.0 };
                double[] xTilts = new double[] { 36.0, -36.0, 36.0, -36.0 };
                for (int i = 0; i < 4; i++)
                {
                    double spd = (i % 2 == 0) ? 1.55 : -1.48;
                    int arcCnt = (orchestrator && (i % 2 == 0)) ? 1 : 0;
                    AddOrbitWithElectrons(1.26, 0.58, 0.015, xTilts[i], (i - 1.5) * 5.0, rolls[i], 2, 0.090, spd, orbitMat, electronMat, electricArcMat, arcCnt);
                }
            }
        }

        private void AddNucleusCluster(int protons, int neutrons, double nucleonRadius, double shellRadius, Material protonMat, Material neutronMat)
        {
            int total = protons + neutrons;
            if (total <= 0)
            {
                return;
            }

            List<Point3D> points = new List<Point3D>();
            int innerCount = (total >= 20) ? Math.Min(8, total / 3) : ((total >= 10) ? 2 : 1);
            int outerCount = total - innerCount;

            if (innerCount == 1)
            {
                points.Add(new Point3D(0.0, 0.0, 0.0));
            }
            else
            {
                double innerR = shellRadius * 0.44;
                double goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
                for (int i = 0; i < innerCount; i++)
                {
                    double y = 1.0 - (i / (double)(innerCount - 1)) * 2.0;
                    double rad = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                    double theta = goldenAngle * i;
                    points.Add(new Point3D(Math.Cos(theta) * rad * innerR, y * innerR, Math.Sin(theta) * rad * innerR));
                }
            }

            double ga = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < outerCount; i++)
            {
                double y = (outerCount == 1) ? 0.0 : (1.0 - (i / (double)(outerCount - 1)) * 2.0);
                double rad = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                double theta = ga * (i + 3);
                double jitter = 0.92 + 0.14 * ((i % 3) * 0.5);
                points.Add(new Point3D(
                    Math.Cos(theta) * rad * shellRadius * jitter,
                    y * shellRadius * jitter,
                    Math.Sin(theta) * rad * shellRadius * jitter));
            }

            int pLeft = protons;
            int nLeft = neutrons;
            for (int i = 0; i < points.Count; i++)
            {
                bool chooseProton;
                if (pLeft == 0)
                {
                    chooseProton = false;
                }
                else if (nLeft == 0)
                {
                    chooseProton = true;
                }
                else
                {
                    chooseProton = (i % 2 == 0);
                }

                if (chooseProton)
                {
                    pLeft--;
                    AddNucleonSphere(points[i].X, points[i].Y, points[i].Z, nucleonRadius, protonMat);
                }
                else
                {
                    nLeft--;
                    AddNucleonSphere(points[i].X, points[i].Y, points[i].Z, nucleonRadius, neutronMat);
                }
            }
        }

        private void AddNucleonSphere(double x, double y, double z, double r, Material mat)
        {
            GeometryModel3D gm = new GeometryModel3D(SharedSphereMesh, mat);
            Transform3DGroup tg = new Transform3DGroup();
            tg.Children.Add(new ScaleTransform3D(r, r, r));
            tg.Children.Add(new TranslateTransform3D(x, y, z));
            gm.Transform = tg;
            atomGroup.Children.Add(gm);
        }

        private void AddOrbitWithElectrons(
            double a, double b, double tubeRadius,
            double rotXDeg, double rotYDeg, double rotZDeg,
            int electronCount, double electronRadius, double speedRadPerSec,
            Material orbitMat, Material electronMat,
            Material electricArcMat, int electricArcCount)
        {
            Matrix3D rotMat = BuildEulerMatrix(rotXDeg, rotYDeg, rotZDeg);

            MeshGeometry3D tubeMesh = BuildEllipticalTubeMesh(a, b, tubeRadius, 68, 10);
            GeometryModel3D ringModel = new GeometryModel3D(tubeMesh, orbitMat);
            ringModel.Transform = new MatrixTransform3D(rotMat);
            atomGroup.Children.Add(ringModel);

            OrbitTrack track = new OrbitTrack();
            track.SemiMajor = a;
            track.SemiMinor = b;
            track.Rotation = rotMat;
            track.SpeedRadPerSec = speedRadPerSec;

            double basePhase = (tracks.Count * 1.15);
            for (int i = 0; i < electronCount; i++)
            {
                double phase = basePhase + (2.0 * Math.PI * i) / electronCount;
                track.ElectronPhases.Add(phase);

                Point3D localPt = new Point3D(a * Math.Cos(phase), b * Math.Sin(phase), 0.0);
                Point3D worldPt = rotMat.Transform(localPt);

                TranslateTransform3D tt = new TranslateTransform3D(worldPt.X, worldPt.Y, worldPt.Z);
                Transform3DGroup etg = new Transform3DGroup();
                etg.Children.Add(new ScaleTransform3D(electronRadius, electronRadius, electronRadius));
                etg.Children.Add(tt);

                GeometryModel3D elModel = new GeometryModel3D(SharedSphereMesh, electronMat);
                elModel.Transform = etg;
                atomGroup.Children.Add(elModel);
                track.ElectronTransforms.Add(tt);
            }

            if (electricArcMat != null && electricArcCount > 0)
            {
                for (int k = 0; k < electricArcCount; k++)
                {
                    int seed = tracks.Count * 31 + k * 17 + 7;
                    // Build arc on unit circle then Rotate(Z) -> Scale(a,b,1) -> rotMat so it tracks the 3D ellipse cleanly!
                    MeshGeometry3D arcMesh = BuildElectricUnitArcMesh(0.90, 0.011, 22, 6, seed);
                    GeometryModel3D arcModel = new GeometryModel3D(arcMesh, electricArcMat);

                    AxisAngleRotation3D zSpin = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0.0);
                    Transform3DGroup atg = new Transform3DGroup();
                    atg.Children.Add(new RotateTransform3D(zSpin));
                    atg.Children.Add(new ScaleTransform3D(a, b, 1.0));
                    atg.Children.Add(new MatrixTransform3D(rotMat));
                    arcModel.Transform = atg;
                    atomGroup.Children.Add(arcModel);

                    ElectricArcBinding binding = new ElectricArcBinding();
                    binding.ZSpin = zSpin;
                    binding.BaseDeg = (tracks.Count * 58.0 + k * 180.0);
                    binding.SpeedDegPerSec = (speedRadPerSec * 180.0 / Math.PI) * 1.45;
                    track.Arcs.Add(binding);
                }
            }

            tracks.Add(track);
        }

        private void AddDashedOrbitWithHollowElectrons(
            double a, double b, double orbitTubeRadius,
            int orbitDashCount, double orbitDutyCycle,
            double rotXDeg, double rotYDeg, double rotZDeg,
            int electronCount, double electronRadius, double electronTubeRadius,
            int electronDashCount, double electronDutyCycle,
            double speedRadPerSec,
            Material orbitMat, Material electronMat)
        {
            Matrix3D rotMat = BuildEulerMatrix(rotXDeg, rotYDeg, rotZDeg);

            MeshGeometry3D dashedOrbitMesh = BuildDashedEllipticalTubeMesh(a, b, orbitTubeRadius, orbitDashCount, orbitDutyCycle, 4, 8);
            GeometryModel3D ringModel = new GeometryModel3D(dashedOrbitMesh, orbitMat);
            ringModel.Transform = new MatrixTransform3D(rotMat);
            atomGroup.Children.Add(ringModel);

            MeshGeometry3D hollowElectronMesh = BuildDashedHollowSphereMesh(electronRadius, electronTubeRadius, electronDashCount, electronDutyCycle, 4, 8);
            Matrix3D electronOrient = BuildEulerMatrix(18.0, 24.0, -12.0);

            OrbitTrack track = new OrbitTrack();
            track.SemiMajor = a;
            track.SemiMinor = b;
            track.Rotation = rotMat;
            track.SpeedRadPerSec = speedRadPerSec;

            double basePhase = 0.65;
            for (int i = 0; i < electronCount; i++)
            {
                double phase = basePhase + (2.0 * Math.PI * i) / electronCount;
                track.ElectronPhases.Add(phase);

                Point3D localPt = new Point3D(a * Math.Cos(phase), b * Math.Sin(phase), 0.0);
                Point3D worldPt = rotMat.Transform(localPt);

                TranslateTransform3D tt = new TranslateTransform3D(worldPt.X, worldPt.Y, worldPt.Z);
                Transform3DGroup etg = new Transform3DGroup();
                etg.Children.Add(new MatrixTransform3D(electronOrient));
                etg.Children.Add(tt);

                GeometryModel3D elModel = new GeometryModel3D(hollowElectronMesh, electronMat);
                elModel.Transform = etg;
                atomGroup.Children.Add(elModel);
                track.ElectronTransforms.Add(tt);
            }

            tracks.Add(track);
        }

        private static Matrix3D BuildEulerMatrix(double xDeg, double yDeg, double zDeg)
        {
            Matrix3D m = Matrix3D.Identity;
            m.Rotate(new Quaternion(new Vector3D(1, 0, 0), xDeg));
            m.Rotate(new Quaternion(new Vector3D(0, 1, 0), yDeg));
            m.Rotate(new Quaternion(new Vector3D(0, 0, 1), zDeg));
            return m;
        }

        private static Material MakeGlossyMaterial(Color diffuseColor, Color emissiveColor, double specPower, byte alpha)
        {
            return MakeMaterialCore(diffuseColor, Colors.White, emissiveColor, specPower, alpha);
        }

        private static Material MakeMetallicMaterial(Color diffuseColor, Color specColor, Color emissiveColor, double specPower, byte alpha)
        {
            return MakeMaterialCore(diffuseColor, specColor, emissiveColor, specPower, alpha);
        }

        private static Material MakeMaterialCore(Color diffuseColor, Color specColor, Color emissiveColor, double specPower, byte alpha)
        {
            Color dc = Color.FromArgb(alpha, diffuseColor.R, diffuseColor.G, diffuseColor.B);
            SolidColorBrush diffBrush = new SolidColorBrush(dc);
            diffBrush.Freeze();

            Color ec = Color.FromArgb(alpha, emissiveColor.R, emissiveColor.G, emissiveColor.B);
            SolidColorBrush emBrush = new SolidColorBrush(ec);
            emBrush.Freeze();

            Color sc = Color.FromArgb(alpha, specColor.R, specColor.G, specColor.B);
            SolidColorBrush specBrush = new SolidColorBrush(sc);
            specBrush.Freeze();

            MaterialGroup mg = new MaterialGroup();
            mg.Children.Add(new DiffuseMaterial(diffBrush));
            mg.Children.Add(new SpecularMaterial(specBrush, specPower));
            mg.Children.Add(new EmissiveMaterial(emBrush));
            mg.Freeze();
            return mg;
        }

        private static Color BlendColor(Color a, Color b, double t)
        {
            double inv = 1.0 - t;
            return Color.FromRgb(
                (byte)Math.Round(a.R * inv + b.R * t),
                (byte)Math.Round(a.G * inv + b.G * t),
                (byte)Math.Round(a.B * inv + b.B * t));
        }

        private static MeshGeometry3D BuildSphereMesh(double radius, int latSegs, int lonSegs)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            for (int lat = 0; lat <= latSegs; lat++)
            {
                double v = lat / (double)latSegs;
                double phi = v * Math.PI;
                double sinPhi = Math.Sin(phi);
                double cosPhi = Math.Cos(phi);

                for (int lon = 0; lon <= lonSegs; lon++)
                {
                    double u = lon / (double)lonSegs;
                    double theta = u * 2.0 * Math.PI;
                    double x = Math.Cos(theta) * sinPhi;
                    double y = cosPhi;
                    double z = Math.Sin(theta) * sinPhi;
                    Vector3D n = new Vector3D(x, y, z);
                    mesh.Positions.Add(new Point3D(x * radius, y * radius, z * radius));
                    mesh.Normals.Add(n);
                }
            }

            int stride = lonSegs + 1;
            for (int lat = 0; lat < latSegs; lat++)
            {
                for (int lon = 0; lon < lonSegs; lon++)
                {
                    int i0 = lat * stride + lon;
                    int i1 = i0 + 1;
                    int i2 = (lat + 1) * stride + lon;
                    int i3 = i2 + 1;

                    mesh.TriangleIndices.Add(i0);
                    mesh.TriangleIndices.Add(i1);
                    mesh.TriangleIndices.Add(i2);

                    mesh.TriangleIndices.Add(i1);
                    mesh.TriangleIndices.Add(i3);
                    mesh.TriangleIndices.Add(i2);
                }
            }

            mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D BuildEllipticalTubeMesh(double a, double b, double tubeRadius, int pathSegs, int crossSegs)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            Vector3D binormal = new Vector3D(0.0, 0.0, 1.0);

            for (int i = 0; i <= pathSegs; i++)
            {
                double t = (2.0 * Math.PI * i) / pathSegs;
                double cosT = Math.Cos(t);
                double sinT = Math.Sin(t);

                Point3D center = new Point3D(a * cosT, b * sinT, 0.0);
                Vector3D tangent = new Vector3D(-a * sinT, b * cosT, 0.0);
                tangent.Normalize();

                Vector3D normal = Vector3D.CrossProduct(tangent, binormal);
                normal.Normalize();

                for (int j = 0; j <= crossSegs; j++)
                {
                    double phi = (2.0 * Math.PI * j) / crossSegs;
                    double cPhi = Math.Cos(phi);
                    double sPhi = Math.Sin(phi);

                    Vector3D radial = normal * cPhi + binormal * sPhi;
                    radial.Normalize();
                    Point3D pt = center + radial * tubeRadius;
                    mesh.Positions.Add(pt);
                    mesh.Normals.Add(radial);
                }
            }

            int ringStride = crossSegs + 1;
            for (int i = 0; i < pathSegs; i++)
            {
                for (int j = 0; j < crossSegs; j++)
                {
                    int p0 = i * ringStride + j;
                    int p1 = p0 + 1;
                    int p2 = (i + 1) * ringStride + j;
                    int p3 = p2 + 1;

                    mesh.TriangleIndices.Add(p0);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p1);

                    mesh.TriangleIndices.Add(p1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p3);
                }
            }

            mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D BuildElectricUnitArcMesh(double sweepRad, double baseTubeRadius, int pathSegs, int crossSegs, int seed)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            Random rng = new Random(seed);
            Point3D[] spine = new Point3D[pathSegs + 1];

            for (int i = 0; i <= pathSegs; i++)
            {
                double u = i / (double)pathSegs;
                double theta = u * sweepRad;
                double env = Math.Sin(Math.PI * u);
                double rJitter = (i == 0 || i == pathSegs) ? 0.0 : ((rng.NextDouble() - 0.5) * 0.036 * env);
                double zJitter = (i == 0 || i == pathSegs) ? 0.0 : ((rng.NextDouble() - 0.5) * 0.036 * env);
                double rad = 1.0 + rJitter;
                spine[i] = new Point3D(rad * Math.Cos(theta), rad * Math.Sin(theta), zJitter);
            }

            Vector3D up = new Vector3D(0.0, 0.0, 1.0);
            for (int i = 0; i <= pathSegs; i++)
            {
                double u = i / (double)pathSegs;
                double taper = 0.25 + 0.75 * Math.Sin(Math.PI * u);
                double r = baseTubeRadius * taper;

                Vector3D tangent;
                if (i == 0)
                {
                    tangent = spine[1] - spine[0];
                }
                else if (i == pathSegs)
                {
                    tangent = spine[pathSegs] - spine[pathSegs - 1];
                }
                else
                {
                    tangent = spine[i + 1] - spine[i - 1];
                }
                if (tangent.LengthSquared < 1e-9)
                {
                    tangent = new Vector3D(0.0, 1.0, 0.0);
                }
                tangent.Normalize();

                Vector3D normal = Vector3D.CrossProduct(tangent, up);
                if (normal.LengthSquared < 1e-6)
                {
                    normal = new Vector3D(1.0, 0.0, 0.0);
                }
                normal.Normalize();
                Vector3D binormal = Vector3D.CrossProduct(normal, tangent);
                binormal.Normalize();

                for (int j = 0; j <= crossSegs; j++)
                {
                    double phi = (2.0 * Math.PI * j) / crossSegs;
                    Vector3D radial = normal * Math.Cos(phi) + binormal * Math.Sin(phi);
                    radial.Normalize();
                    mesh.Positions.Add(spine[i] + radial * r);
                    mesh.Normals.Add(radial);
                }
            }

            int ringStride = crossSegs + 1;
            for (int i = 0; i < pathSegs; i++)
            {
                for (int j = 0; j < crossSegs; j++)
                {
                    int p0 = i * ringStride + j;
                    int p1 = p0 + 1;
                    int p2 = (i + 1) * ringStride + j;
                    int p3 = p2 + 1;

                    mesh.TriangleIndices.Add(p0);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p1);

                    mesh.TriangleIndices.Add(p1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p3);
                }
            }

            mesh.Freeze();
            return mesh;
        }

        private static void AppendCappedTubeSegment(
            MeshGeometry3D mesh,
            Point3D[] spine,
            Vector3D[] normals,
            Vector3D[] binormals,
            double tubeRadius,
            int crossSegs)
        {
            int count = spine.Length;
            if (count < 2)
            {
                return;
            }

            int baseIdx = mesh.Positions.Count;
            for (int i = 0; i < count; i++)
            {
                Point3D center = spine[i];
                Vector3D normal = normals[i];
                Vector3D binormal = binormals[i];
                for (int j = 0; j <= crossSegs; j++)
                {
                    double phi = (2.0 * Math.PI * j) / crossSegs;
                    Vector3D radial = normal * Math.Cos(phi) + binormal * Math.Sin(phi);
                    radial.Normalize();
                    mesh.Positions.Add(center + radial * tubeRadius);
                    mesh.Normals.Add(radial);
                }
            }

            int stride = crossSegs + 1;
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < crossSegs; j++)
                {
                    int p0 = baseIdx + i * stride + j;
                    int p1 = p0 + 1;
                    int p2 = baseIdx + (i + 1) * stride + j;
                    int p3 = p2 + 1;

                    mesh.TriangleIndices.Add(p0);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p1);

                    mesh.TriangleIndices.Add(p1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p3);
                }
            }

            // Start cap (outward normal = -startTangent)
            Vector3D startCapNormal = Vector3D.CrossProduct(normals[0], binormals[0]);
            if (startCapNormal.LengthSquared < 1e-9)
            {
                startCapNormal = spine[0] - spine[1];
            }
            startCapNormal.Normalize();

            int startCenterIdx = mesh.Positions.Count;
            mesh.Positions.Add(spine[0]);
            mesh.Normals.Add(startCapNormal);
            int startRimBase = mesh.Positions.Count;
            for (int j = 0; j <= crossSegs; j++)
            {
                double phi = (2.0 * Math.PI * j) / crossSegs;
                Vector3D radial = normals[0] * Math.Cos(phi) + binormals[0] * Math.Sin(phi);
                radial.Normalize();
                mesh.Positions.Add(spine[0] + radial * tubeRadius);
                mesh.Normals.Add(startCapNormal);
            }
            for (int j = 0; j < crossSegs; j++)
            {
                mesh.TriangleIndices.Add(startCenterIdx);
                mesh.TriangleIndices.Add(startRimBase + j);
                mesh.TriangleIndices.Add(startRimBase + j + 1);
            }

            // End cap (outward normal = +endTangent)
            int last = count - 1;
            Vector3D endCapNormal = Vector3D.CrossProduct(binormals[last], normals[last]);
            if (endCapNormal.LengthSquared < 1e-9)
            {
                endCapNormal = spine[last] - spine[last - 1];
            }
            endCapNormal.Normalize();

            int endCenterIdx = mesh.Positions.Count;
            mesh.Positions.Add(spine[last]);
            mesh.Normals.Add(endCapNormal);
            int endRimBase = mesh.Positions.Count;
            for (int j = 0; j <= crossSegs; j++)
            {
                double phi = (2.0 * Math.PI * j) / crossSegs;
                Vector3D radial = normals[last] * Math.Cos(phi) + binormals[last] * Math.Sin(phi);
                radial.Normalize();
                mesh.Positions.Add(spine[last] + radial * tubeRadius);
                mesh.Normals.Add(endCapNormal);
            }
            for (int j = 0; j < crossSegs; j++)
            {
                mesh.TriangleIndices.Add(endCenterIdx);
                mesh.TriangleIndices.Add(endRimBase + j + 1);
                mesh.TriangleIndices.Add(endRimBase + j);
            }
        }

        private static MeshGeometry3D BuildDashedEllipticalTubeMesh(
            double a,
            double b,
            double tubeRadius,
            int dashCount,
            double dutyCycle,
            int segsPerDash,
            int crossSegs)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();
            const int arcSamples = 360;
            double[] cumLen = new double[arcSamples + 1];
            Point3D prevPt = new Point3D(a, 0.0, 0.0);
            cumLen[0] = 0.0;
            for (int k = 1; k <= arcSamples; k++)
            {
                double t = (2.0 * Math.PI * k) / arcSamples;
                Point3D curPt = new Point3D(a * Math.Cos(t), b * Math.Sin(t), 0.0);
                cumLen[k] = cumLen[k - 1] + (curPt - prevPt).Length;
                prevPt = curPt;
            }

            double totalLen = cumLen[arcSamples];
            double slotLen = totalLen / dashCount;
            double halfGap = slotLen * (1.0 - dutyCycle) * 0.5;
            Vector3D binormal = new Vector3D(0.0, 0.0, 1.0);

            int sampleCursor = 0;
            for (int d = 0; d < dashCount; d++)
            {
                double sStart = d * slotLen + halfGap;
                double sEnd = (d + 1) * slotLen - halfGap;

                Point3D[] spine = new Point3D[segsPerDash + 1];
                Vector3D[] normals = new Vector3D[segsPerDash + 1];
                Vector3D[] binormals = new Vector3D[segsPerDash + 1];

                for (int i = 0; i <= segsPerDash; i++)
                {
                    double u = i / (double)segsPerDash;
                    double targetS = sStart + (sEnd - sStart) * u;
                    while (sampleCursor < arcSamples - 1 && cumLen[sampleCursor + 1] < targetS)
                    {
                        sampleCursor++;
                    }
                    double segLen = cumLen[sampleCursor + 1] - cumLen[sampleCursor];
                    double frac = (segLen > 1e-9) ? ((targetS - cumLen[sampleCursor]) / segLen) : 0.0;
                    double t = (2.0 * Math.PI * (sampleCursor + frac)) / arcSamples;

                    double cosT = Math.Cos(t);
                    double sinT = Math.Sin(t);
                    spine[i] = new Point3D(a * cosT, b * sinT, 0.0);

                    Vector3D tangent = new Vector3D(-a * sinT, b * cosT, 0.0);
                    tangent.Normalize();
                    Vector3D normal = Vector3D.CrossProduct(tangent, binormal);
                    normal.Normalize();

                    normals[i] = normal;
                    binormals[i] = binormal;
                }

                AppendCappedTubeSegment(mesh, spine, normals, binormals, tubeRadius, crossSegs);
            }

            mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D BuildDashedHollowSphereMesh(
            double radius,
            double tubeRadius,
            int dashCount,
            double dutyCycle,
            int segsPerDash,
            int crossSegs)
        {
            MeshGeometry3D mesh = new MeshGeometry3D();

            Vector3D[] ringU = new Vector3D[]
            {
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(1.0, 0.0, 0.0),
                new Vector3D(0.0, 1.0, 0.0)
            };
            Vector3D[] ringV = new Vector3D[]
            {
                new Vector3D(0.0, 1.0, 0.0),
                new Vector3D(0.0, 0.0, 1.0),
                new Vector3D(0.0, 0.0, 1.0)
            };

            double step = (2.0 * Math.PI) / dashCount;
            double halfGap = step * (1.0 - dutyCycle) * 0.5;

            for (int rIdx = 0; rIdx < 3; rIdx++)
            {
                Vector3D uVec = ringU[rIdx];
                Vector3D vVec = ringV[rIdx];
                Vector3D wVec = Vector3D.CrossProduct(uVec, vVec);
                wVec.Normalize();

                for (int d = 0; d < dashCount; d++)
                {
                    double t0 = d * step + halfGap;
                    double t1 = (d + 1) * step - halfGap;

                    Point3D[] spine = new Point3D[segsPerDash + 1];
                    Vector3D[] normals = new Vector3D[segsPerDash + 1];
                    Vector3D[] binormals = new Vector3D[segsPerDash + 1];

                    for (int i = 0; i <= segsPerDash; i++)
                    {
                        double t = t0 + (t1 - t0) * (i / (double)segsPerDash);
                        Vector3D radial = uVec * Math.Cos(t) + vVec * Math.Sin(t);
                        radial.Normalize();
                        spine[i] = new Point3D(radial.X * radius, radial.Y * radius, radial.Z * radius);
                        normals[i] = radial;
                        binormals[i] = wVec;
                    }

                    AppendCappedTubeSegment(mesh, spine, normals, binormals, tubeRadius, crossSegs);
                }
            }

            mesh.Freeze();
            return mesh;
        }
    }
}
