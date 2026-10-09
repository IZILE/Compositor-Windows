using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Compositor.Desktop;

/// <summary>Small vector marks keep the footer independent of symbol-font availability.</summary>
internal sealed class FooterIcon(int kind) : Control
{
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(Skin.LabelBrush, 1.3) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var offset = new Point((Bounds.Width - 18) / 2, (Bounds.Height - 18) / 2);
        Point At(double x, double y) => new(offset.X + x, offset.Y + y);
        void Line(double x, double y, double otherX, double otherY) => context.DrawLine(pen, At(x,y), At(otherX,otherY));
        switch (kind)
        {
            case 0:
                context.DrawRectangle(null, pen, new Rect(At(2,2), At(16,16)), 2, 2);
                Line(5,9,13,9); Line(9,5,9,13);
                break;
            case 1:
                Line(1,5,1,15); Line(1,15,17,15); Line(17,15,17,5); Line(17,5,8,5);
                Line(8,5,6,3); Line(6,3,1,3); Line(1,3,1,5); Line(6,10,12,10); Line(9,7,9,13);
                break;
            case 2:
                context.DrawRectangle(null, pen, new Rect(At(1,3), At(17,15)), 2, 2);
                context.DrawEllipse(null, pen, At(9,9), 3.5, 3.5);
                break;
            case 3:
                Line(8,2,10,7); Line(10,7,15,9); Line(15,9,10,11); Line(10,11,8,16);
                Line(8,16,6,11); Line(6,11,1,9); Line(1,9,6,7); Line(6,7,8,2);
                Line(15,1,15,5); Line(13,3,17,3);
                break;
            case 4:
                context.DrawEllipse(null, pen, At(9,9), 7, 7);
                using (context.PushClip(new Rect(At(2,2), At(9,16)))) context.DrawEllipse(Skin.LabelBrush, null, At(9,9), 7, 7);
                break;
            default:
                Line(3,4,15,4); Line(6,2,12,2); Line(4,4,5,16); Line(5,16,13,16); Line(13,16,14,4);
                Line(7,7,7,13); Line(11,7,11,13);
                break;
        }
    }
}
