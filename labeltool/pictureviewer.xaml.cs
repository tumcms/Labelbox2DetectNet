using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace labeltool
{
    /// <inheritdoc>
    ///     <cref></cref>
    /// </inheritdoc>
    /// <summary>
    /// Interaktionslogik für pictureviewer.xaml
    /// </summary>
    public partial class Pictureviewer
    {
        private string MyTitle
        {
            set => SetValue(MyTitleProperty, value);
        }

        public static readonly DependencyProperty MyTitleProperty =
            DependencyProperty.Register("MyTitle", typeof(string), typeof(Pictureviewer), new UIPropertyMetadata(null));

        private const int MyWidth = 800;
        private const int MyHeight = 600;
        private readonly Brush _myStroke = Brushes.Lime;
        private const double MyStrokeThickness = 2;


        public Pictureviewer(string myUrl, IEnumerable<Tuple<string, List<Point>>> myLabels)
        {
            InitializeComponent();

            SizeToContent = SizeToContent.WidthAndHeight;
            DataContext = this;
            MyTitle = myUrl;
            
            Bitmap bitmap = MainWindow.LoadBitmap(myUrl);
            BitmapImage img = ConvertBitmapImage(bitmap);
            
            ImageBrush ib = new ImageBrush
            {
                ImageSource = img
            };
            Grid.Background = ib;
            double imgWidth = img.PixelWidth;
            double imgHeight = img.PixelHeight;

            double imgratiox = MyWidth / imgWidth;
            double imgratioy = MyHeight / imgHeight;

            foreach (Tuple<string, List<Point>> label in myLabels)
            {
                Polygon mypolygon = new Polygon();
                foreach (Point curpt in label.Item2)
                {
                    mypolygon.Points.Add(PointParser(curpt,imgratiox, imgratioy));
                }
                mypolygon.Stroke = _myStroke;
                mypolygon.StrokeThickness = MyStrokeThickness;
                Canvas.Children.Add(mypolygon);
            }
        }

        private static Point PointParser(Point pts, double imgratiox, double imgratioy)
        {
            double x = pts.X * imgratiox;
            double y = MyHeight - pts.Y * imgratioy;
            return new Point(x, y);
        }

        private static BitmapImage ConvertBitmapImage(Image src)
        {
            MemoryStream ms = new MemoryStream();
            src.Save(ms, ImageFormat.Bmp);
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            ms.Seek(0, SeekOrigin.Begin);
            image.StreamSource = ms;
            image.EndInit();
            return image;
        }
    }
}