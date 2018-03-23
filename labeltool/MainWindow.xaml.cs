using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Newtonsoft.Json;
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
            public string MyMultipolygon { get; set; }
            public List<Tuple<string,List<Point>>> LabeledList { get; set; }
        }

        private List<MyLabelData> _myLabels;
        private BackgroundWorker _myWorker;
        private string _myFolder;

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
                        Tuple<string, List<Point>> curLabelTupel = new Tuple<string, List<Point>>(labeltitle,labPoints);
                        labeledList.Add(curLabelTupel);
                    }
                }

                _myLabels.Add(new MyLabelData
                {
                    MyUrl = thisUrl,
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
                string[] fileNameSplit = myFileName.Split('.');
                File.WriteAllText(_myFolder + "\\labels\\" + fileNameSplit[0] + ".txt", label.MyMultipolygon);

                using (WebClient client = new WebClient())
                {
                    client.DownloadFile(label.MyUrl, _myFolder + "\\images\\" + myFileName);
                }

                backgroundWorker?.ReportProgress(i / allLabels * 100);
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
                    Rectangle boundingBox = GetBoundingBox(labelTuple.Item2, source.Height);
                    Bitmap croppedImage = source.Clone(boundingBox, source.PixelFormat);
                    croppedImage.Save(_myFolder + "\\" + labelTuple.Item1 + "_" + i + "_" + j + ".jpg",
                        ImageFormat.Jpeg);
                    backgroundWorker?.ReportProgress(i / allLabels * 100);
                    i++;
                    j++;
                }


                
            }
        }

        private static Rectangle GetBoundingBox(IReadOnlyCollection<Point> labelList, int height)
        {
            int x = Convert.ToInt32(labelList.Min(point => point.X));
            int y = height - Convert.ToInt32(labelList.Max(point => point.Y));
            int width = Convert.ToInt32(labelList.Max(point => point.X) - x);
            int heigth = Convert.ToInt32(labelList.Max(point => point.Y) - Convert.ToInt32(labelList.Min(point => point.Y)));

            Rectangle myRectangle = new Rectangle(x, y, width, heigth);
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
    }
}