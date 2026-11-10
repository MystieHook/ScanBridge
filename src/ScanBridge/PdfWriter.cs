using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ScanBridge
{
    // Minimal image-only PDF writer. JPEG pages are embedded as DCT streams.
    internal static class PdfWriter
    {
        internal static void Write(ScanJob job, string output)
        {
            var pages = job.pages.Where(p => !p.excluded).ToList();
            if (pages.Count == 0) throw new InvalidOperationException("No pages to export.");
            using (var stream = new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {
                var offsets = new List<long> { 0 };
                Action<string> text = s => { var bytes = Encoding.ASCII.GetBytes(s); stream.Write(bytes,0,bytes.Length); };
                Action<int,string> obj = (id,body) => { while (offsets.Count <= id) offsets.Add(0); offsets[id]=stream.Position; text(id+" 0 obj\n"+body+"\nendobj\n"); };
                text("%PDF-1.4\n%ScanBridge\n");
                obj(1,"<< /Type /Catalog /Pages 2 0 R >>");
                obj(2,"<< /Type /Pages /Count "+pages.Count+" /Kids ["+string.Join(" ",Enumerable.Range(0,pages.Count).Select(i=>(3+i*3)+" 0 R"))+"] >>");
                int pageIndex=0;
                foreach(var page in pages)
                using(var image=ImageProcessing.Process(job.PagePath(page),job.profile,page.rotation))
                using(var jpeg=new MemoryStream())
                {
                    var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");
                    using(var parameters=new EncoderParameters(1))
                    { parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,90L); image.Save(jpeg,codec,parameters); }
                    int pageId=3+pageIndex*3, imageId=pageId+1, contentId=pageId+2;
                    string width=(image.Width*72.0/job.profile.dpi).ToString("0.###",CultureInfo.InvariantCulture);
                    string height=(image.Height*72.0/job.profile.dpi).ToString("0.###",CultureInfo.InvariantCulture);
                    obj(pageId,"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "+width+" "+height+"] /Resources << /XObject << /Image "+imageId+" 0 R >> >> /Contents "+contentId+" 0 R >>");
                    while(offsets.Count<=imageId) offsets.Add(0); offsets[imageId]=stream.Position;
                    text(imageId+" 0 obj\n<< /Type /XObject /Subtype /Image /Width "+image.Width+" /Height "+image.Height+" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length "+jpeg.Length+" >>\nstream\n");
                    jpeg.Position=0; jpeg.CopyTo(stream); text("\nendstream\nendobj\n");
                    string commands="q\n"+width+" 0 0 "+height+" 0 0 cm\n/Image Do\nQ\n";
                    obj(contentId,"<< /Length "+Encoding.ASCII.GetByteCount(commands)+" >>\nstream\n"+commands+"endstream");
                    pageIndex++;
                }
                long xref=stream.Position;
                text("xref\n0 "+offsets.Count+"\n0000000000 65535 f \n");
                foreach(long offset in offsets.Skip(1)) text(offset.ToString("D10",CultureInfo.InvariantCulture)+" 00000 n \n");
                text("trailer\n<< /Size "+offsets.Count+" /Root 1 0 R >>\nstartxref\n"+xref+"\n%%EOF\n");
                stream.Flush(true);
            }
        }
    }
}
