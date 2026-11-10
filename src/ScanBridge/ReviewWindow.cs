using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScanBridge
{
    internal sealed class ReviewWindow : Window
    {
        private readonly ScanJob job;
        private readonly ListBox pages = new ListBox { Width = 210, Margin = new Thickness(0,0,16,0) };
        private readonly Image preview = new Image { Stretch = Stretch.Uniform };
        private readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,0) };
        private readonly Button save = new Button { Content = "Save PDF", Padding = new Thickness(20,10,20,10), Margin = new Thickness(8) };
        internal ReviewWindow(ScanJob job)
        {
            this.job = job;
            using(var resources=typeof(ReviewWindow).Assembly.GetManifestResourceStream("ScanBridge.MainWindow.xaml"))
                Resources=((FrameworkElement)System.Windows.Markup.XamlReader.Load(resources)).Resources;
            Title = "Review scan · ScanBridge"; Width=1050; Height=800; MinWidth=700; MinHeight=500;
            Icon=AppVisuals.WindowIcon();
            FontFamily = new FontFamily("Segoe UI"); FontSize=14; Background=new SolidColorBrush(Color.FromRgb(244,246,250));
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var layout=new DockPanel { Margin=new Thickness(24) }; Content=layout;
            var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); layout.Children.Add(header);
            header.Children.Add(new TextBlock { Text="Review your scan",FontSize=27,FontWeight=FontWeights.SemiBold });
            header.Children.Add(new TextBlock { Text="Rotate pages or hide unwanted pages. Originals remain available for recovery.", Margin=new Thickness(0,7,0,12) });
            var footer=new StackPanel(); DockPanel.SetDock(footer,Dock.Bottom);layout.Children.Add(footer);
            var actions=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
            var rotate=new Button { Content="Rotate 90°",Padding=new Thickness(16,10,16,10),Margin=new Thickness(8) };
            var toggle=new Button { Content="Hide / include page",Padding=new Thickness(16,10,16,10),Margin=new Thickness(8) };
            var close=new Button { Content="Keep for later",Padding=new Thickness(16,10,16,10),Margin=new Thickness(8) };
            actions.Children.Add(rotate);actions.Children.Add(toggle);actions.Children.Add(close);actions.Children.Add(save);footer.Children.Add(actions);footer.Children.Add(status);
            layout.Children.Add(pages);layout.Children.Add(new Border { Background=Brushes.White,CornerRadius=new CornerRadius(14),Padding=new Thickness(12),Child=preview });
            pages.SelectionChanged+=(s,e)=>ShowPage();
            rotate.Click+=(s,e)=> { int i=pages.SelectedIndex;if(i<0)return;job.pages[i].rotation=(job.pages[i].rotation+90)%360;job.Save();RefreshPages(i); };
            toggle.Click+=(s,e)=> { int i=pages.SelectedIndex;if(i<0)return;job.pages[i].excluded=!job.pages[i].excluded;job.Save();RefreshPages(i); };
            close.Click+=(s,e)=>Close();
            save.Click+=async(s,e)=>await Export();
            RefreshPages(0);
        }
        private void RefreshPages(int selected)
        {
            pages.Items.Clear();
            for(int i=0;i<job.pages.Count;i++)
            { var p=job.pages[i];pages.Items.Add("Page "+(i+1)+(p.excluded?" · hidden":"")+(p.otherSide?" · other side":"")+(p.blank?" · blank":"")); }
            if(pages.Items.Count>0)pages.SelectedIndex=Math.Min(selected,pages.Items.Count-1);
            status.Text=job.pages.Count(p=>!p.excluded)+" included · "+job.profile.destination;
        }
        private void ShowPage()
        {
            if(pages.SelectedIndex<0)return;
            try
            {
                var page=job.pages[pages.SelectedIndex];
                using(var image=ImageProcessing.Process(job.PagePath(page),job.profile,page.rotation))
                using(var bytes=new MemoryStream())
                {
                    image.Save(bytes,System.Drawing.Imaging.ImageFormat.Bmp);bytes.Position=0;
                    var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.DecodePixelWidth=Math.Min(1200,image.Width);source.StreamSource=bytes;source.EndInit();source.Freeze();preview.Source=source;
                }
            }
            catch(Exception ex){status.Text="Preview unavailable: "+ex.Message;preview.Source=null;}
        }
        private async Task Export()
        {
            IsEnabled=false;
            try { string output=await Task.Run(()=>ScanJobs.Export(job));status.Text="Saved: "+output; }
            catch(Exception ex){MessageBox.Show(this,"The scan is kept for recovery. "+ex.Message,"Could not save PDF",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{IsEnabled=true;}
        }
    }
}
