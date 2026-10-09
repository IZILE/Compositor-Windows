using Compositor.Core.Document;
using Compositor.Core.Format;
using Compositor.Core.IO;
using Compositor.Core.Model;
using SkiaSharp;

namespace Compositor.Core.Tests;

public sealed class MaterialTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CompositorMaterials-"+Guid.NewGuid().ToString("N"));
    private string File(string name,string text) { Directory.CreateDirectory(_root); var path=Path.Combine(_root,name); System.IO.File.WriteAllText(path,text); return path; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root,true); }
    public static IEnumerable<object[]> Shapes => MaterialPresets.Shapes.Select(p=>new object[]{p.Kind});

    [Theory,MemberData(nameof(Shapes))]
    public void EveryBuiltInShapeRendersAndSurvivesEditableProjectRoundTrip(ShapeKind kind)
    {
        using var document = new CanvasDocument(Guid.NewGuid(),160,120);
        var id=ShapeEdits.Add(document,new LayerShapeStyle {Kind=kind,Red=1,LineWidth=3},SKRectI.Create(10,10,80,60),null);
        Assert.NotNull(id); var layer=Assert.Single(document.Layers);
        Assert.Contains(layer.Asset!.Image.Pixels,color=>color.Alpha>0);
        var path=Path.Combine(_root,kind+".comp"); ProjectStore.Save(ProjectSnapshot.FromDocument(document),path);
        using var loaded=ProjectStore.Load(path); using var reopened=loaded.ToDocument();
        Assert.Equal(kind,Assert.Single(reopened.Layers).LiveShape!.Kind);
        Assert.Equal(kind>ShapeKind.Line?12:11,loaded.Manifest.Version);
        var resized=ShapeEdits.Scaled(reopened.Layers[0],160,120)!.Value;
        Assert.Equal(160,resized.Asset.Width); resized.Asset.Dispose();
    }
    [Fact]
    public void SvgTransformsHolesAndOverlappingElementsKeepTheirSilhouetteAtNewSizes()
    {
        var path=File("shape.svg","""
        <svg xmlns="http://www.w3.org/2000/svg"><g transform="translate(12 17) scale(2)">
          <path fill-rule="evenodd" d="M0 0H20V20H0Z M5 5H15V15H5Z"/>
          <rect x="18" y="0" width="4" height="20"/>
        </g></svg>
        """);
        var preset=Assert.Single(MaterialImport.Shapes(path));
        using var document=new CanvasDocument(Guid.NewGuid(),100,100);
        ShapeEdits.Add(document,new LayerShapeStyle {Kind=preset.Kind,PathData=preset.PathData,EvenOdd=preset.EvenOdd?true:null,Red=1},SKRectI.Create(0,0,44,40),null);
        var layer=document.Layers[0]; Assert.Equal(0,layer.Asset!.Image.GetPixel(20,20).Alpha);
        Assert.Equal(255,layer.Asset.Image.GetPixel(38,20).Alpha); // Overlap is a union, not XOR.
        var package=Path.Combine(_root,"svg.comp"); ProjectStore.Save(ProjectSnapshot.FromDocument(document),package);
        using var loaded=ProjectStore.Load(package); using var after=loaded.ToDocument();
        Assert.Equal(preset.PathData,after.Layers[0].LiveShape!.PathData);
        var resized=ShapeEdits.Scaled(after.Layers[0],88,80)!.Value;
        Assert.Equal(0,resized.Asset.Image.GetPixel(40,40).Alpha); Assert.Equal(255,resized.Asset.Image.GetPixel(76,40).Alpha); resized.Asset.Dispose();
    }
    [Fact]
    public void SvgChildCanOverrideAnInheritedNoFill()
    {
        var preset=MaterialImport.Shapes(File("inherit.svg","<svg fill=\"none\"><rect fill=\"red\" width=\"10\" height=\"10\"/></svg>"));
        Assert.Single(preset);
    }
    [Theory]
    [InlineData("<svg><text>text</text></svg>")]
    [InlineData("<svg><path stroke=\"red\" d=\"M0 0H10V10Z\"/></svg>")]
    [InlineData("<svg><rect style=\"fill:red\" width=\"10\" height=\"10\"/></svg>")]
    [InlineData("<svg><rect width=\"NaN\" height=\"10\"/></svg>")]
    public void UnsupportedSvgDoesNotPretendToImportCorrectly(string text) => Assert.ThrowsAny<Exception>(()=>MaterialImport.Shapes(File("bad.svg",text)));
    [Fact]
    public void SvgDtdCannotReadExternalFiles() => Assert.Throws<System.Xml.XmlException>(()=>MaterialImport.Shapes(File("external.svg","<!DOCTYPE svg [<!ENTITY x SYSTEM 'file:///never-read'>]><svg>&x;</svg>")));
    [Fact]
    public void GgrMidpointsDiscontinuitiesAndAlphaArePreserved()
    {
        var preset=Assert.Single(MaterialImport.Gradients(File("gradient.ggr","""
        GIMP Gradient
        Name: Test
        2
        0 0.1 0.5 1 0 0 1 0 0 1 0.5 0 0
        0.5 0.75 1 0 1 0 1 1 1 1 1 0 0
        """)));
        Assert.Equal(new SKColor(128,0,128,192),preset.Sample(.1));
        Assert.Equal(SKColors.Lime,preset.Sample(.5)); Assert.Equal(SKColors.White,preset.Sample(1));
        var reversed=preset.Reversed(); Assert.Equal(SKColors.White,reversed.Sample(0)); Assert.Equal(SKColors.Red,reversed.Sample(1));
    }
    [Theory]
    [InlineData("0 0.5 1 1 0 0 1 0 0 1 1 1 0")]
    [InlineData("0 0.5 1 1 0 0 1 0 0 1 1 0 1")]
    [InlineData("0 NaN 1 1 0 0 1 0 0 1 1 0 0")]
    public void UnsupportedGgrRulesAreExplicitlyRejected(string segment) => Assert.Throws<InvalidDataException>(()=>MaterialImport.Gradients(File("bad.ggr","GIMP Gradient\nName: Bad\n1\n"+segment)));
    [Fact]
    public void GplImportKeepsRgbAndNames()
    {
        var colors=MaterialImport.Colors(File("palette.gpl","GIMP Palette\nName: Test\nColumns: 2\n# comment\n255 0 128 Rose pink\n0 20 40 深蓝"));
        Assert.Equal(2,colors.Count); Assert.Equal("Rose pink",colors[0].Name); Assert.Equal((uint)new SKColor(0,20,40),colors[1].Color);
    }
    [Fact]
    public void PatternImageKeepsAlphaAndFillsOnlyTheSelectionInDocumentCoordinates()
    {
        Directory.CreateDirectory(_root); var path=Path.Combine(_root,"tile.png");
        using(var tile=new SKBitmap(new SKImageInfo(2,1,SKColorType.Rgba8888,SKAlphaType.Unpremul)))
        { tile.SetPixel(0,0,SKColors.Red); tile.SetPixel(1,0,SKColors.Transparent); using var data=tile.Encode(SKEncodedImageFormat.Png,100); System.IO.File.WriteAllBytes(path,data.ToArray()); }
        var pattern=Assert.Single(MaterialImport.Patterns(path));
        Assert.Equal(SKColors.Red,pattern.Sample(new SKPoint(-2,0))); Assert.Equal(0,pattern.Sample(new SKPoint(-1,0)).Alpha);
        using var document=new CanvasDocument(Guid.NewGuid(),8,4);
        var id=ShapeEdits.Add(document,new LayerShapeStyle {Kind=ShapeKind.Rectangle,Blue=1},SKRectI.Create(0,0,8,4),null)!.Value;
        SelectionEdits.Select(document,SKRectI.Create(2,1,4,2));
        Assert.True(GradientEdits.FillPattern(document,id,false,pattern)); var pixels=document.Layers[0].Asset!.Image;
        Assert.Equal(SKColors.Blue,pixels.GetPixel(0,0)); Assert.Equal(SKColors.Red,pixels.GetPixel(2,1)); Assert.Equal(SKColors.Blue,pixels.GetPixel(3,1));
    }
    [Theory]
    [InlineData(GradientShape.Angle,0,10,.25)]
    [InlineData(GradientShape.Reflected,-5,0,.5)]
    [InlineData(GradientShape.Diamond,2,3,.5)]
    public void NewlyExposedGradientModesHaveDistinctGeometry(GradientShape shape,float x,float y,double expected) =>
        Assert.Equal(expected,GradientEdits.Parameter(new SKPoint(0,0),new SKPoint(10,0),new SKPoint(x,y),shape),6);
    [Fact]
    public void InvalidImportCannotOverwriteAnExistingLibrary()
    {
        Directory.CreateDirectory(_root); var path=Path.Combine(_root,"gradients.json");
        MaterialLibrary.Save(path,MaterialPresets.Gradients.ToArray()); var held=System.IO.File.ReadAllBytes(path);
        Assert.Throws<InvalidDataException>(()=>MaterialLibrary.Save(path,new[]{new GradientPreset("Bad",[new(double.NaN,0),new(1,0)])}));
        Assert.Equal(held,System.IO.File.ReadAllBytes(path)); Assert.Equal(12,MaterialLibrary.Load<GradientPreset>(path).Length);
        Assert.Empty(Directory.GetFiles(_root,"*.tmp"));
    }
    [Fact]
    public void BuiltInBrushesAreDistinctUsableStaticMasks()
    {
        Assert.Equal(14,MaterialPresets.Brushes.Count);
        var tips=MaterialPresets.Brushes.Where(p=>p.Tip is not null).Select(p=>p.Tip!).ToArray();
        Assert.Equal(tips.Length,tips.DistinctBy(t=>t.ID).Count()); Assert.All(tips,t=>Assert.Contains(t.Alpha.ToArray(),a=>a>0));
    }
}
