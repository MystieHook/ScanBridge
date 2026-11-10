using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class BuildIcon
{
    static GraphicsPath Round(float x,float y,float width,float height,float radius)
    {
        var path=new GraphicsPath();float d=radius*2;
        path.AddArc(x,y,d,d,180,90);path.AddArc(x+width-d,y,d,d,270,90);
        path.AddArc(x+width-d,y+height-d,d,d,0,90);path.AddArc(x,y+height-d,d,d,90,90);path.CloseFigure();return path;
    }
    static Bitmap Draw(int size)
    {
        // Render at 4x for crisp small-size antialiasing; all geometry is code-native vector.
        using(var canvas=new Bitmap(size*4,size*4,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(canvas))
        {
            g.SmoothingMode=SmoothingMode.AntiAlias;g.ScaleTransform(size*4/128f,size*4/128f);
            using(var path=Round(4,4,120,120,28))
            using(var blue=new LinearGradientBrush(new PointF(4,4),new PointF(124,124),ColorTranslator.FromHtml("#4B85FF"),ColorTranslator.FromHtml("#2456C4")))g.FillPath(blue,path);
            using(var page=Round(40,22,48,58,7))g.FillPath(Brushes.White,page);
            if(size>=24)
                using(var line=new Pen(ColorTranslator.FromHtml("#A5BEF3"),5){StartCap=LineCap.Round,EndCap=LineCap.Round}){g.DrawLine(line,51,36,77,36);g.DrawLine(line,51,46,70,46);}
            using(var scanner=Round(23,62,82,40,12))g.FillPath(Brushes.White,scanner);
            using(var slot=new Pen(ColorTranslator.FromHtml("#2456C4"),7){StartCap=LineCap.Round,EndCap=LineCap.Round})g.DrawLine(slot,39,67,89,67);
            using(var arch=new GraphicsPath())
            using(var fill=new SolidBrush(ColorTranslator.FromHtml("#2D65D8")))
            {arch.AddLine(46,102,46,97);arch.AddArc(46,79,36,36,180,180);arch.AddLine(82,97,82,102);arch.CloseFigure();g.FillPath(fill,arch);}
            using(var light=new SolidBrush(ColorTranslator.FromHtml("#24C7B5")))g.FillEllipse(light,90,77,8,8);
            var output=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            using(var resize=Graphics.FromImage(output)){resize.InterpolationMode=InterpolationMode.HighQualityBicubic;resize.DrawImage(canvas,0,0,size,size);}
            return output;
        }
    }
    static int Main(string[] args)
    {
        string folder=args.Length>0?Path.GetFullPath(args[0]):@"C:\Scansnap\assets";Directory.CreateDirectory(folder);
        int[] sizes={16,20,24,32,40,48,64,128,256};var images=new List<byte[]>();
        foreach(int size in sizes)using(var image=Draw(size))using(var data=new MemoryStream())
        {image.Save(data,ImageFormat.Png);images.Add(data.ToArray());if(size==256)image.Save(Path.Combine(folder,"ScanBridge-preview.png"),ImageFormat.Png);}
        using(var file=File.Create(Path.Combine(folder,"ScanBridge.ico")))using(var writer=new BinaryWriter(file))
        {
            writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(images[i].Length);writer.Write(offset);offset+=images[i].Length;}
            foreach(var image in images)writer.Write(image);
        }
        Console.WriteLine("Created SVG-matched ICO with "+sizes.Length+" sizes.");return 0;
    }
}
