using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScanBridge
{
    internal static class AppVisuals
    {
        internal static ImageSource WindowIcon()
        {
            using(var stream=typeof(AppVisuals).Assembly.GetManifestResourceStream("ScanBridge.ScanBridge.ico"))
            {
                var decoder=new IconBitmapDecoder(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                var frame=decoder.Frames[decoder.Frames.Count-1];frame.Freeze();return frame;
            }
        }
        internal static Icon TrayIcon()
        {
            using(var stream=typeof(AppVisuals).Assembly.GetManifestResourceStream("ScanBridge.ScanBridge.ico"))
            using(var icon=new Icon(stream,new Size(32,32)))return (Icon)icon.Clone();
        }
    }
}
