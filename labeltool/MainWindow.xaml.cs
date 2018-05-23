using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using ListView = System.Windows.Controls.ListView;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Point = System.Windows.Point;

namespace labeltool
{
    /// <inheritdoc>
    ///     <cref></cref>
    /// </inheritdoc>
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        private class MyLabelData
        {
            public string MyUrl { get; set; }
            public string Filename { get; set; }
            public string MyMultipolygon { get; set; }
            public List<Tuple<string,List<Point>>> LabeledList { get; set; }
        }

        private List<MyLabelData> _myLabels;
        private BackgroundWorker _myWorker;
        private string _myFolder;
        private byte[] _colorArray;
        private int _stride;

        public MainWindow()
        {
            InitializeComponent();
        }

        public class Labelbox
        {
            [JsonProperty("ID")] public string Id { get; set; }

            [JsonProperty("Labeled Data")] public string LabeledData { get; set; }

            [JsonProperty("Label")] public dynamic Label { get; set; }

            [JsonProperty("Created By")] public string CreatedBy { get; set; }

            [JsonProperty("Project Name")] public string ProjectName { get; set; }

            [JsonProperty("Seconds to Label")] public string SecondsToLabel { get; set; }

            [JsonProperty("External ID")] public string ExternalId { get; set; }
        }


        private void ImportJson(object sender, RoutedEventArgs e)
        {
            OpenFileDialog myFileDialog = new OpenFileDialog
            {
                Filter = "Labelbox JSON Export|*.json",
                Title = "Import a LabelBox.io JSON file"
            };
            if (myFileDialog.ShowDialog() != true)
            {
                return;
            }

            string myJson = File.ReadAllText(myFileDialog.FileName);

            List<Labelbox> myLabels = JsonConvert.DeserializeObject<List<Labelbox>>(myJson);

            _myLabels = new List<MyLabelData>();
            foreach (Labelbox myLabel in myLabels)
            {
                string thisUrl = myLabel.LabeledData;
                string[] filename = thisUrl.Split('/');
                filename = filename.Last().Split('.');
                string curLabel = null;
                List<Tuple<string,List<Point>>> labeledList = new List<Tuple<string, List<Point>>>();
                if(myLabel.Label.ToString() == "Skip") continue;
                foreach (dynamic multilabel in myLabel.Label)
                {
                    string labeltitle = multilabel.Name.ToString();
                    

                    
                    foreach (dynamic singlelabel in multilabel.Value)
                    {
                        if (curLabel != null)
                        {
                            curLabel += Environment.NewLine;
                        }

                        curLabel += labeltitle;
                        List<Point> labPoints = new List<Point>();

                        foreach (dynamic coords in singlelabel)
                        {
                            int x = Convert.ToInt32(coords.x.Value);
                            int y = Convert.ToInt32(coords.y.Value);
                            labPoints.Add(new Point(x,y));
                            curLabel += " " + x + " " + y;
                        }

                        if (labPoints.Count <= 1) continue;
                        Tuple<string, List<Point>> curLabelTupel = new Tuple<string, List<Point>>(labeltitle, labPoints);
                        labeledList.Add(curLabelTupel);
                    }
                }

                _myLabels.Add(new MyLabelData
                {
                    MyUrl = thisUrl,
                    Filename = filename.First(),
                    MyMultipolygon = curLabel,
                    LabeledList = labeledList
                });
            }

            LabelList.ItemsSource = _myLabels;
            LblStatusbarInfo.Text = "JSON loaded successfully.";
        }

        private void CreateFolderStructure(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog myDiag = new FolderBrowserDialog();
            DialogResult result = myDiag.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;
            if (_myLabels == null) return;

            _myFolder = myDiag.SelectedPath;
            Directory.CreateDirectory(_myFolder + "\\images");
            Directory.CreateDirectory(_myFolder + "\\labels");

            _myWorker = new BackgroundWorker();
            _myWorker.DoWork += WriteDataToFolder;
            _myWorker.ProgressChanged += UpdateProgressBar;
            _myWorker.RunWorkerCompleted += FinishedFolderCreation;
            _myWorker.WorkerReportsProgress = true;
            _myWorker.RunWorkerAsync();
        }

        private void FinishedFolderCreation(object sender, RunWorkerCompletedEventArgs e)
        {
            LblStatusbarInfo.Text = "Folder structure created";
            StatusProgressBar.Value = 0;
        }

        private void UpdateProgressBar(object sender, ProgressChangedEventArgs e)
        {
            StatusProgressBar.Value = e.ProgressPercentage;
        }

        private void WriteDataToFolder(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker backgroundWorker = sender as BackgroundWorker;
            int allLabels = _myLabels.Count;
            int i = 1;
            foreach (MyLabelData label in _myLabels)
            {
                string[] urlSplit = label.MyUrl.Split('/');
                string myFileName = urlSplit[urlSplit.Length - 1];

                //     1    type         Describes the type of object: 'Car', 'Van', 'Truck',
                // 'Pedestrian', 'Person_sitting', 'Cyclist', 'Tram',
                // 'Misc' or 'DontCare'
                // 1    truncated    Float from 0 (non-truncated) to 1 (truncated), where
                // truncated refers to the object leaving image boundaries
                // 1    occluded     Integer (0,1,2,3) indicating occlusion state:
                // 0 = fully visible, 1 = partly occluded
                // 2 = largely occluded, 3 = unknown
                // 1    alpha        Observation angle of object, ranging [-pi..pi]
                // 4    bbox         2D bounding box of object in the image (0-based index):
                // contains left, top, right, bottom pixel coordinates
                // 3    dimensions   3D object dimensions: height, width, length (in meters)
                // 3    location     3D object location x,y,z in camera coordinates (in meters)
                // 1    rotation_y   Rotation ry around Y-axis in camera coordinates [-pi..pi]
                // 1    score        Only for results: Float, indicating confidence in
                // detection, needed for p/r curves, higher is better.

                Bitmap source = LoadBitmap(label.MyUrl);
                StringBuilder allLabelContent = new StringBuilder();
                foreach (Tuple<string, List<Point>> labelTuple in label.LabeledList)
                {
                    Rectangle boundingBox = GetBoundingBox(labelTuple.Item2, source.Width, source.Height);

                    double maxX = Math.Round(Convert.ToDouble(boundingBox.X + boundingBox.Width),2);
                    double maxY = Math.Round(Convert.ToDouble(boundingBox.Y + boundingBox.Height), 2);
                    double minX = Math.Round(Convert.ToDouble(boundingBox.X), 2);
                    double minY = Math.Round(Convert.ToDouble(boundingBox.Y), 2);

                    string labelcontent = "Misc 0.00 0 0.0 " + minX + ".00 " + minY + ".00 " + maxX + ".00 " + maxY + ".00 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0";
                    allLabelContent.AppendLine(labelcontent);
                }

                File.WriteAllText(_myFolder + "\\labels\\" + label.Filename + ".txt", allLabelContent.ToString());

                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(label.MyUrl, _myFolder + "\\images\\" + myFileName);
                }

                double percentage = i / (double)allLabels;
                int progressPercentage = Convert.ToInt32(percentage * 100);
                backgroundWorker?.ReportProgress(progressPercentage);
                i++;
            }
        }

        private void LabelList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            MyLabelData selectedLabel = (MyLabelData) ((ListView) sender).SelectedItem;
            if (selectedLabel == null) return;

            Pictureviewer pictureviewer = new Pictureviewer(selectedLabel.MyUrl, selectedLabel.LabeledList);
            pictureviewer.Show();
        }

        private void ExportSnippets(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog myDiag = new FolderBrowserDialog();
            DialogResult result = myDiag.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;
            if (_myLabels == null) return;

            _myFolder = myDiag.SelectedPath;

            _myWorker = new BackgroundWorker();
            _myWorker.DoWork += LoadImgForSnippets;
            _myWorker.ProgressChanged += UpdateProgressBar;
            _myWorker.RunWorkerCompleted += FinishedFolderCreation;
            _myWorker.WorkerReportsProgress = true;
            _myWorker.RunWorkerAsync();
        }

        private void LoadImgForSnippets(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker backgroundWorker = sender as BackgroundWorker;
            int i = 1;
            int allLabels = _myLabels.Count;
            foreach (MyLabelData myLabelData in _myLabels)
            {
                Bitmap source = LoadBitmap(myLabelData.MyUrl);

                int j = 1;
                foreach (Tuple<string, List<Point>> labelTuple in myLabelData.LabeledList)
                {
                    Rectangle boundingBox = GetBoundingBox(labelTuple.Item2, source.Width, source.Height);
                    if(boundingBox.Width < 200 || boundingBox.Height < 200) continue;
                    string filename = _myFolder + "\\" + myLabelData.Filename + "_" + labelTuple.Item1 + "_" + j + ".jpg";

                    SaveCroppedImage(filename, source, boundingBox);
                    j++;
                }
                double percentage = i / (double)allLabels;
                int progressPercentage = Convert.ToInt32(percentage * 100);
                backgroundWorker?.ReportProgress(progressPercentage);
                i++;
            }
        }

        private static void SaveCroppedImage(string filename, Bitmap source, Rectangle boundingBox)
        {
            Bitmap croppedImage = source.Clone(boundingBox, source.PixelFormat);
            croppedImage.Save(filename, ImageFormat.Jpeg);
        }


        private static Rectangle GetBoundingBox(IReadOnlyCollection<Point> labelList, int width, int height)
        {
            int maxX = Convert.ToInt32(labelList.Max(point => point.X));
            if (maxX > width)
            {
                maxX = width;
            }
            int y = Convert.ToInt32(labelList.Max(point => point.Y));
            if (y > height)
            {
                y = height;
            }

            int x = Convert.ToInt32(labelList.Min(point => point.X));
            if (x < 0)
            {
                x = 0;
            }
            int minY = Convert.ToInt32(labelList.Min(point => point.Y));
            if (minY < 0)
            {
                minY = 0;
            }
            int boxwidth = maxX - x;
            int boxheight = y - minY;

            //y = height - y;

            Rectangle myRectangle = new Rectangle(x, y, boxwidth, boxheight);
            return myRectangle;
        }


        public static Bitmap LoadBitmap(string url)
        {
            WebClient wc = new WebClient();

            byte[] originalData = wc.DownloadData(url);
            MemoryStream stream = new MemoryStream(originalData);
            Bitmap myBitmap = new Bitmap(stream);
            stream.Flush();
            return myBitmap;
        }

        private void ExportMasks(object sender, RoutedEventArgs e)
        {
            FolderBrowserDialog myDiag = new FolderBrowserDialog();
            DialogResult result = myDiag.ShowDialog();
            if (result != System.Windows.Forms.DialogResult.OK) return;
            if (_myLabels == null) return;

            _myFolder = myDiag.SelectedPath;
            _myWorker = new BackgroundWorker();
            _myWorker.DoWork += CreateMasks;
            _myWorker.ProgressChanged += UpdateProgressBar;
            _myWorker.RunWorkerCompleted += FinishedFolderCreation;
            _myWorker.WorkerReportsProgress = true;
            _myWorker.RunWorkerAsync();
        }

        private void CreateMasks(object sender, DoWorkEventArgs e)
        {
            BackgroundWorker backgroundWorker = sender as BackgroundWorker;

            int i = 1;
            int allLabels = _myLabels.Count;
            foreach (MyLabelData myLabelData in _myLabels)
            {
                Bitmap source = LoadBitmap(myLabelData.MyUrl);
                WriteableBitmap bmp = new WriteableBitmap(source.Width, source.Height, 96, 96, PixelFormats.Indexed8, BitmapPalettes.Halftone256);
                Int32Rect rect = new Int32Rect(0, 0, bmp.PixelWidth, bmp.PixelHeight);
                int bytesPerPixel = (bmp.Format.BitsPerPixel + 7) / 8;
                _stride = bmp.PixelWidth * bytesPerPixel;
                int arraySize = _stride * bmp.PixelHeight;
                _colorArray = new byte[arraySize];
                const byte myrgb = 1;

                foreach (Tuple<string, List<Point>> tuple in myLabelData.LabeledList)
                {
                    PixelLabelColoring(tuple.Item2, myrgb, source.Width, source.Height);
                }
                bmp.WritePixels(rect, _colorArray, _stride, 0);

                using (FileStream outStream = new FileStream(_myFolder + "\\" + myLabelData.Filename + ".png", FileMode.Create))
                {
                    PngBitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    encoder.Save(outStream);
                }

                backgroundWorker?.ReportProgress(i / allLabels * 100);
                i++;
            }
        }

        private void PixelLabelColoring(IReadOnlyCollection<Point> pointlist, byte myrgb, int imageWidth, int imageHeight)
        {
            // BoundingBox within given Image
            int minX = Convert.ToInt32(pointlist.Min(point => point.X));
            int maxX = Convert.ToInt32(pointlist.Max(point => point.X));
            int minY = Convert.ToInt32(pointlist.Min(point => point.Y));
            int maxY = Convert.ToInt32(pointlist.Max(point => point.Y));
            minX = minX > 1 ? minX : 1;
            maxX = maxX > imageWidth ? imageWidth : maxX;
            minY = minY > 1 ? minY : 1;
            maxY = maxY > imageHeight ? imageHeight : maxY;
            if ((minX >= imageWidth) && (minY >= imageHeight)) return;

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    //double s = (CrossProduct(q, v2)) / (CrossProduct(v1, v2));
                    //double t = (CrossProduct(v1, q)) / (CrossProduct(v1, v2));

                    //if ((!(s >= 0)) || (!(t >= 0)) || (!(s + t <= 1))) continue;
                    //double currentZ = CalcZ(p1, p2, p3, x, y);
                    //int mydistpos = MyWidth * (y - 1) + (x - 1);
                    //if (_distanceArray[mydistpos] != null && !(_distanceArray[mydistpos] > currentZ))
                    //    continue;

                    int myxinarray = x - 1;
                    int myyinarray = (y - 1) * _stride;
                    int start = myyinarray + myxinarray;
                    _colorArray[start] = myrgb;
                }
            }
        }

        //private bool AreIntersecting()
        //{

        //}
    }
}